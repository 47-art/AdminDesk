using System.Text;
using AdminDesk.Application.Documents;
using AdminDesk.Application.Engine;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;

namespace AdminDesk.Infrastructure.Requests;

internal sealed class ProbeDocumentRow
{
    public long Id { get; set; }
    public long IsActive { get; set; }
    public string? DeletedUtc { get; set; }
    public string? DeletedByName { get; set; }
    public string StoredName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

// Checks 26 to 28: documents on a request, the proof of delivery step that needs one, and who may
// change or only read them. They run last because they add document events to the audit trail.
internal sealed class EngineProbeDocumentChecks
{
    private const string Step = "pod-upload";

    private static readonly byte[] SamplePdf =
        Encoding.ASCII.GetBytes("%PDF-1.4\n% probe document\n").Concat(Enumerable.Range(0, 1000).Select(i => (byte)(i % 251))).ToArray();

    private readonly ProbeKit _k;

    public EngineProbeDocumentChecks(ProbeKit kit)
    {
        _k = kit;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await ProofOfDeliveryAsync();
        await RemovalAndRolesAsync();
        await FinishLeftoverCouriersAsync();
    }

    // ---------------------------------------------------------------- helpers

    private static readonly object Courier = new
    {
        documentDescription = "Agreement",
        senderName = "Priya Nair",
        receiverName = "Receiver Docs",
        receiverAddress = "1 Example Road",
        receiverCity = "Pune"
    };

    // A courier request taken to the proof of delivery step.
    private async Task<long> CourierAtProofAsync()
    {
        var id = await _k.CreateAsync(_k.Requester, "courier", Courier);
        await _k.ActAsync(_k.Admin, id, RequestAction.Complete, null, new() { ["courierCompany"] = ProbeKit.Json("Example Couriers") });
        await _k.ActAsync(_k.Admin, id, RequestAction.Complete);
        await _k.ActAsync(_k.Admin, id, RequestAction.Complete, null, new() { ["trackingNumber"] = ProbeKit.Json("TRK-DOC-1") });
        await _k.ActAsync(_k.Requester, id, RequestAction.Complete);
        var request = await _k.RequestAsync(id);
        _k.Check(request.CurrentStatus == "InProgress" && request.CurrentStepKey == Step, "26: the courier request did not reach pod-upload");
        return id;
    }

    private Task<DocumentDto> UploadAsync(
        ActorContext actor, long requestId, string name, byte[]? bytes = null, string? contentType = "application/pdf", string? step = Step)
    {
        var data = bytes ?? SamplePdf;
        return _k.Documents.UploadAsync(actor, requestId, new DocumentUpload(name, contentType, data.Length, new MemoryStream(data)), step, default);
    }

    private async Task<(byte[] Bytes, DocumentDownload Download)> DownloadAsync(ActorContext actor, long requestId, long documentId)
    {
        var download = await _k.Documents.DownloadAsync(actor, requestId, documentId, default);
        await using var content = download.Content;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        return (buffer.ToArray(), download);
    }

    private async Task<ProbeDocumentRow> RowAsync(long documentId) =>
        (await _k.QueryAsync<ProbeDocumentRow>(
            "SELECT id, is_active AS IsActive, deleted_utc AS DeletedUtc, deleted_by_name AS DeletedByName, " +
            "stored_name AS StoredName, original_name AS OriginalName, content_type AS ContentType, size_bytes AS SizeBytes " +
            "FROM request_documents WHERE id = @Id", new { Id = documentId })).Single();

    private Task<long> DocumentRowCountAsync(long requestId) =>
        _k.ScalarAsync<long>("SELECT COUNT(*) FROM request_documents WHERE request_id = @Id", new { Id = requestId });

    private async Task ExpectDocumentErrorAsync(string what, string code, string field, Func<Task> call)
    {
        var ex = await _k.ExpectAsync<ValidationException>(what, call);
        _k.Check(ex.Code == code, $"{what}: expected code {code} but got {ex.Code}");
        _k.Check(ex.FieldErrors.Any(e => e.Field == field), $"{what}: no error keyed to '{field}'");
    }

    // ------------------------------------------------------------ 26 and 27

    private async Task ProofOfDeliveryAsync()
    {
        var p = await CourierAtProofAsync();
        var fingerprint = await _k.FingerprintAsync(p);

        // The step cannot be completed without a document.
        var missing = await _k.ExpectAsync<ValidationException>("26: complete pod-upload without a document",
            () => _k.ActAsync(_k.Admin, p, RequestAction.Complete));
        _k.Check(missing.Code == ErrorCodes.DOCUMENT_REQUIRED && missing.FieldErrors.Any(e => e.Field == "file"),
            $"26: the missing document was reported as {missing.Code}");
        _k.Check(await _k.FingerprintAsync(p) == fingerprint, "26: the refused completion changed the request");

        // Refused uploads leave no row and no audit event.
        await ExpectDocumentErrorAsync("26: executable upload", ErrorCodes.DOCUMENT_TYPE_NOT_ALLOWED, "file",
            () => UploadAsync(_k.Admin, p, "payload.exe", null, "application/octet-stream"));
        await ExpectDocumentErrorAsync("26: html upload", ErrorCodes.DOCUMENT_TYPE_NOT_ALLOWED, "file",
            () => UploadAsync(_k.Admin, p, "page.html", null, "text/html"));
        await ExpectDocumentErrorAsync("26: a pdf name with a web page content type", ErrorCodes.DOCUMENT_TYPE_NOT_ALLOWED, "file",
            () => UploadAsync(_k.Admin, p, "disguised.pdf", null, "text/html"));
        await ExpectDocumentErrorAsync("26: no extension", ErrorCodes.DOCUMENT_TYPE_NOT_ALLOWED, "file",
            () => UploadAsync(_k.Admin, p, "proof", null, "application/pdf"));
        var tooBig = new byte[_k.DocumentLimits.MaxBytes + 1];
        await ExpectDocumentErrorAsync("26: file over the maximum", ErrorCodes.DOCUMENT_TOO_LARGE, "file",
            () => UploadAsync(_k.Admin, p, "big.pdf", tooBig));
        await _k.ExpectAsync<ValidationException>("26: empty file", () => UploadAsync(_k.Admin, p, "empty.pdf", Array.Empty<byte>()));
        await ExpectDocumentErrorAsync("26: a step the request does not have", ErrorCodes.VALIDATION_FAILED, "stepKey",
            () => UploadAsync(_k.Admin, p, "proof.pdf", null, "application/pdf", "no-such-step"));
        _k.Check(await DocumentRowCountAsync(p) == 0 && await _k.AuditCountAsync(p, AuditEventTypes.DocumentUploaded) == 0,
            "26: a refused upload left a row or an audit event");

        // Read-only roles and people who cannot see the request.
        await _k.ExpectNotAllowedAsync("26: management upload", () => UploadAsync(_k.Management, p, "proof.pdf"));
        await _k.ExpectNotAllowedAsync("26: system admin upload", () => UploadAsync(_k.SysAdmin, p, "proof.pdf"));
        await _k.ExpectHiddenAsync("26: upload by someone who cannot see the request", () => UploadAsync(_k.Uninvolved, p, "proof.pdf"));
        await _k.ExpectHiddenAsync("26: list by someone who cannot see the request", () => _k.Documents.ListAsync(_k.Uninvolved, p, default));
        _k.Check(await DocumentRowCountAsync(p) == 0, "26: a refused actor stored a document");

        // A real upload by the Admin who holds the step; the path in the name is dropped and the stored name is generated.
        var uploaded = await UploadAsync(_k.Admin, p, "..\\..\\scans/proof of delivery.pdf");
        var row = await RowAsync(uploaded.Id);
        _k.Check(uploaded.OriginalName == "proof of delivery.pdf" && row.OriginalName == "proof of delivery.pdf",
            $"26: the file name was kept as '{row.OriginalName}'");
        _k.Check(row.StoredName != row.OriginalName && row.StoredName.Length == 36 && row.StoredName.EndsWith(".pdf", StringComparison.Ordinal)
            && row.StoredName.All(c => char.IsAsciiHexDigitLower(c) || c == '.' || c == 'p' || c == 'd' || c == 'f'),
            $"26: the stored name '{row.StoredName}' is not a generated one");
        _k.Check(row.ContentType == "application/pdf" && row.SizeBytes == SamplePdf.Length && uploaded.StepKey == Step && row.IsActive == 1,
            "26: the stored document details are wrong");
        _k.Check(await _k.AuditCountAsync(p, AuditEventTypes.DocumentUploaded) == 1, "26: the upload left no audit event");
        var details = await _k.ScalarAsync<string>(
            "SELECT details_json FROM audit_events WHERE request_id = @Id AND event_type = 'DocumentUploaded'", new { Id = p });
        _k.Check(details.Contains("proof of delivery.pdf", StringComparison.Ordinal) && details.Contains(SamplePdf.Length.ToString(), StringComparison.Ordinal),
            "26: the upload audit details do not carry the file name and size");

        // Download is byte for byte, for the requester too, and is audited.
        var (bytes, download) = await DownloadAsync(_k.Requester, p, uploaded.Id);
        _k.Check(bytes.SequenceEqual(SamplePdf) && download.FileName == "proof of delivery.pdf" && download.ContentType == "application/pdf",
            "26: the downloaded file is not the uploaded one");
        _k.Check(await _k.AuditCountAsync(p, AuditEventTypes.DocumentDownloaded) == 1, "26: the download left no audit event");
        await _k.ExpectHiddenAsync("26: download by someone who cannot see the request", () => DownloadAsync(_k.Uninvolved, p, uploaded.Id));
        await _k.ExpectHiddenAsync("26: download of a document under another request", async () =>
        {
            var other = await CourierAtProofAsync();
            await DownloadAsync(_k.Admin, other, uploaded.Id);
        });
        _k.Check(await _k.AuditCountAsync(p, AuditEventTypes.DocumentDownloaded) == 1, "26: a refused download left an audit event");

        // Management and the system admin may read: list shows the file, but nothing is removable by them.
        var asManagement = await _k.Documents.ListAsync(_k.Management, p, default);
        var asAdmin = await _k.Documents.ListAsync(_k.Admin, p, default);
        var asRequester = await _k.Documents.ListAsync(_k.Requester, p, default);
        _k.Check(asManagement.Count == 1 && !asManagement[0].CanRemove, "26: management can remove");
        _k.Check(asAdmin.Count == 1 && asAdmin[0].CanRemove, "26: the uploading admin cannot remove");
        _k.Check(asRequester.Count == 1 && !asRequester[0].CanRemove, "26: the requester can remove someone else's file");
        var (readOnlyBytes, _) = await DownloadAsync(_k.Management, p, uploaded.Id);
        _k.Check(readOnlyBytes.SequenceEqual(SamplePdf), "26: management could not download the file");

        // With a document in place the same completion now succeeds and the request closes.
        await _k.ActAsync(_k.Admin, p, RequestAction.Complete);
        var closed = await _k.RequestAsync(p);
        _k.Check(closed.CurrentStatus == "Closed" && closed.ClosedUtc is not null, "26: the request did not close after the document was uploaded");
        await _k.ExpectNotAllowedAsync("26: upload to a closed request", () => UploadAsync(_k.Admin, p, "late.pdf"));
        _k.Pass("26 documents and the proof of delivery step");
    }

    // ----------------------------------------------------------------- 27, 28

    private async Task RemovalAndRolesAsync()
    {
        var q = await CourierAtProofAsync();
        var byRequester = await UploadAsync(_k.Requester, q, "receipt.png", SamplePdf, "image/png");
        var byAdmin = await UploadAsync(_k.Admin, q, "signed.docx", SamplePdf,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", null);
        _k.Check(byRequester.StepKey == Step && byAdmin.StepKey is null, "27: the step keys were not stored as given");

        // Only the uploader and the Admin remove; read-only roles and outsiders do not.
        await _k.ExpectNotAllowedAsync("27: the requester removing the admin's file", () => _k.Documents.RemoveAsync(_k.Requester, q, byAdmin.Id, default));
        await _k.ExpectNotAllowedAsync("27: management removing a file", () => _k.Documents.RemoveAsync(_k.Management, q, byRequester.Id, default));
        await _k.ExpectNotAllowedAsync("27: system admin removing a file", () => _k.Documents.RemoveAsync(_k.SysAdmin, q, byRequester.Id, default));
        await _k.ExpectHiddenAsync("27: removal by someone who cannot see the request", () => _k.Documents.RemoveAsync(_k.Uninvolved, q, byRequester.Id, default));
        _k.Check(await _k.AuditCountAsync(q, AuditEventTypes.DocumentRemoved) == 0, "27: a refused removal left an audit event");

        // The Admin removes the requester's proof; the row and the file stay.
        await _k.Documents.RemoveAsync(_k.Admin, q, byRequester.Id, default);
        var removed = await RowAsync(byRequester.Id);
        _k.Check(removed.IsActive == 0 && removed.DeletedUtc is not null && removed.DeletedByName == _k.Admin.Name,
            "27: the removed document is not marked removed by the admin");
        await using (var stillThere = _k.Files.OpenRead(removed.StoredName))
        {
            _k.Check(stillThere.Length == SamplePdf.Length, "27: the stored file was changed by the removal");
        }
        var listed = await _k.Documents.ListAsync(_k.Admin, q, default);
        _k.Check(listed.Count == 1 && listed[0].Id == byAdmin.Id, "27: the removed document is still listed");
        await _k.ExpectHiddenAsync("27: download of a removed document", () => DownloadAsync(_k.Admin, q, byRequester.Id));
        await _k.ExpectHiddenAsync("27: removing a document twice", () => _k.Documents.RemoveAsync(_k.Admin, q, byRequester.Id, default));
        _k.Check(await _k.AuditCountAsync(q, AuditEventTypes.DocumentRemoved) == 1, "27: the removal left no single audit event");
        _k.Check(await DocumentRowCountAsync(q) == 2, "27: a document row went missing");

        // A removed document no longer counts, and a document for no step does not either.
        var stillMissing = await _k.ExpectAsync<ValidationException>("27: complete with only a removed document",
            () => _k.ActAsync(_k.Admin, q, RequestAction.Complete));
        _k.Check(stillMissing.Code == ErrorCodes.DOCUMENT_REQUIRED, "27: removed or unrelated documents satisfied the step");

        // The uploader removes their own file.
        var again = await UploadAsync(_k.Requester, q, "receipt-2.jpg", SamplePdf, "image/jpeg");
        await _k.Documents.RemoveAsync(_k.Requester, q, again.Id, default);
        _k.Check((await RowAsync(again.Id)).IsActive == 0, "27: the uploader could not remove their own file");

        var proof = await UploadAsync(_k.Admin, q, "proof-2.pdf");
        await _k.ActAsync(_k.Admin, q, RequestAction.Complete);
        _k.Check((await _k.RequestAsync(q)).CurrentStatus == "Closed" && proof.Id > 0, "27: the request did not close");
        await _k.ExpectNotAllowedAsync("27: removal on a closed request", () => _k.Documents.RemoveAsync(_k.Admin, q, proof.Id, default));
        _k.Check((await RowAsync(proof.Id)).IsActive == 1, "27: a document on a closed request was removed");
        _k.Pass("27 soft removal and who may remove");

        // 28: the list, the download and the audit trail stay readable for the read-only roles, who cannot change anything.
        var readable = await _k.Documents.ListAsync(_k.SysAdmin, q, default);
        _k.Check(readable.Count == 2 && readable.All(d => !d.CanRemove), "28: the system admin list is wrong");
        _k.Check(await _k.AuditCountAsync(q, AuditEventTypes.DocumentUploaded) == 4, "28: not every upload was audited");
        _k.Pass("28 read-only roles");
    }

    // The core checks leave two couriers waiting at the proof step; they are finished here once a document exists.
    private async Task FinishLeftoverCouriersAsync()
    {
        foreach (var id in _k.CourierAtProof)
        {
            await _k.ExpectAsync<ValidationException>("26: complete the leftover courier without a document",
                () => _k.ActAsync(_k.Admin, id, RequestAction.Complete));
            await UploadAsync(_k.Admin, id, "proof.pdf");
            await _k.ActAsync(_k.Admin, id, RequestAction.Complete);
            _k.Check((await _k.RequestAsync(id)).CurrentStatus == "Closed", "26: a courier request did not close after its proof of delivery");
        }
        _k.Pass("26 couriers closed after the proof of delivery");
    }
}
