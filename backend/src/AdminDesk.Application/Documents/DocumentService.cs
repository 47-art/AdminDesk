using System.Text.Json;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Requests;
using AdminDesk.SharedKernel.Actors;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;

namespace AdminDesk.Application.Documents;

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentDto>> ListAsync(ActorContext actor, long requestId, CancellationToken ct);

    Task<DocumentDto> UploadAsync(ActorContext actor, long requestId, DocumentUpload upload, string? stepKey, CancellationToken ct);

    Task<DocumentDownload> DownloadAsync(ActorContext actor, long requestId, long documentId, CancellationToken ct);

    Task RemoveAsync(ActorContext actor, long requestId, long documentId, CancellationToken ct);
}

// Files attached to a request. A request the caller cannot see answers as not found on every
// call. Changing documents needs an in-progress request and a person who takes part in it;
// Management and the SystemAdmin only read. Upload, download and removal each leave an audit row
// written in the same transaction as the metadata change. Removal only marks the row.
public sealed class DocumentService : IDocumentService
{
    private const int NameLimit = 200;

    // The extension decides the type. The browser's content type must not contradict it; the stored
    // and served type always comes from this table, never from the client.
    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    private static readonly IReadOnlySet<string> NeutralContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "application/octet-stream", "image/jpg", "image/pjpeg" };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDbConnectionFactory _factory;
    private readonly IRequestRepository _requests;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentFileStore _files;
    private readonly IAuditRepository _audit;
    private readonly RequestAccessPolicy _access;
    private readonly IDefinitionProvider _definitions;
    private readonly IActorAccessor _actorAccessor;
    private readonly TimeProvider _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly DocumentSettings _settings;

    public DocumentService(
        IUnitOfWork unitOfWork,
        IDbConnectionFactory factory,
        IRequestRepository requests,
        IDocumentRepository documents,
        IDocumentFileStore files,
        IAuditRepository audit,
        RequestAccessPolicy access,
        IDefinitionProvider definitions,
        IActorAccessor actorAccessor,
        TimeProvider clock,
        ICorrelationIdAccessor correlation,
        DocumentSettings settings)
    {
        _unitOfWork = unitOfWork;
        _factory = factory;
        _requests = requests;
        _documents = documents;
        _files = files;
        _audit = audit;
        _access = access;
        _definitions = definitions;
        _actorAccessor = actorAccessor;
        _clock = clock;
        _correlation = correlation;
        _settings = settings;
    }

    private sealed record RequestAccess(
        RequestSnapshot Request, IReadOnlyList<RequestStepRow> Steps, IReadOnlyList<ActorRow> Actors);

    // ------------------------------------------------------------------ list

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(ActorContext actor, long requestId, CancellationToken ct)
    {
        await RequireVisibleAsync(actor, requestId, ct);
        await using var connection = await _factory.OpenAsync(ct);
        var access = await LoadAccessAsync(connection, requestId, ct);
        var rows = await _documents.ListAsync(connection, requestId, ct);
        var canChange = CanChange(actor, access);
        return rows.Select(row => ToDto(row, canChange && CanRemove(actor, row))).ToList();
    }

    // ---------------------------------------------------------------- upload

    public async Task<DocumentDto> UploadAsync(
        ActorContext actor, long requestId, DocumentUpload upload, string? stepKey, CancellationToken ct)
    {
        _actorAccessor.Use(actor.UserId);
        await RequireVisibleAsync(actor, requestId, ct);

        RequestAccess access;
        await using (var connection = await _factory.OpenAsync(ct))
        {
            access = await LoadAccessAsync(connection, requestId, ct);
        }
        if (!CanChange(actor, access))
        {
            throw NotAllowed();
        }

        var (name, extension) = ValidateFile(upload);
        var step = await ValidateStepAsync(access.Request, stepKey, ct);

        var storedName = Guid.NewGuid().ToString("N") + extension;
        await _files.SaveAsync(storedName, upload.Content, ct);

        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var role = RoleLabel(actor, access);
            var id = await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
            {
                var current = await _requests.GetSnapshotAsync(tx, requestId, ct)
                    ?? throw new NotFoundException("Request not found.", ErrorCodes.NOT_FOUND);
                if (current.CurrentStatus != RequestStatus.InProgress)
                {
                    throw NotAllowed();
                }

                var newId = await _documents.InsertAsync(tx, new NewDocument(
                    requestId, step, name, storedName, upload.Length, ContentTypes[extension],
                    actor.UserId, actor.Name, now), ct);
                await AppendAuditAsync(tx, requestId, AuditEventTypes.DocumentUploaded, actor, role, step, name, upload.Length, now, ct);
                return newId;
            }, ct);

            return new DocumentDto(id, name, upload.Length, ContentTypes[extension], step, actor.Name, now, true);
        }
        catch
        {
            _files.DiscardUnsaved(storedName);
            throw;
        }
    }

    // -------------------------------------------------------------- download

    public async Task<DocumentDownload> DownloadAsync(ActorContext actor, long requestId, long documentId, CancellationToken ct)
    {
        _actorAccessor.Use(actor.UserId);
        await RequireVisibleAsync(actor, requestId, ct);

        RequestAccess access;
        await using (var connection = await _factory.OpenAsync(ct))
        {
            access = await LoadAccessAsync(connection, requestId, ct);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var role = RoleLabel(actor, access);
        var row = await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            var found = await _documents.GetAsync(tx, requestId, documentId, ct)
                ?? throw new NotFoundException("Document not found.", ErrorCodes.NOT_FOUND);
            await AppendAuditAsync(tx, requestId, AuditEventTypes.DocumentDownloaded, actor, role, found.StepKey,
                found.OriginalName, found.SizeBytes, now, ct);
            return found;
        }, ct);

        return new DocumentDownload(_files.OpenRead(row.StoredName), row.OriginalName, row.ContentType);
    }

    // ---------------------------------------------------------------- remove

    public async Task RemoveAsync(ActorContext actor, long requestId, long documentId, CancellationToken ct)
    {
        _actorAccessor.Use(actor.UserId);
        await RequireVisibleAsync(actor, requestId, ct);

        RequestAccess access;
        await using (var connection = await _factory.OpenAsync(ct))
        {
            access = await LoadAccessAsync(connection, requestId, ct);
        }
        if (!CanChange(actor, access))
        {
            throw NotAllowed();
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var role = RoleLabel(actor, access);
        await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            var found = await _documents.GetAsync(tx, requestId, documentId, ct)
                ?? throw new NotFoundException("Document not found.", ErrorCodes.NOT_FOUND);
            if (!CanRemove(actor, found))
            {
                throw NotAllowed();
            }

            await _documents.SoftDeleteAsync(tx, found.Id, actor.Name, ct);
            await AppendAuditAsync(tx, requestId, AuditEventTypes.DocumentRemoved, actor, role, found.StepKey,
                found.OriginalName, found.SizeBytes, now, ct);
            return true;
        }, ct);
    }

    // ----------------------------------------------------------------- rules

    private async Task RequireVisibleAsync(ActorContext actor, long requestId, CancellationToken ct)
    {
        if (!await _access.CanViewAsync(actor, requestId, ct))
        {
            throw new NotFoundException("Request not found.", ErrorCodes.NOT_FOUND);
        }
    }

    private async Task<RequestAccess> LoadAccessAsync(System.Data.Common.DbConnection connection, long requestId, CancellationToken ct)
    {
        var request = await _requests.GetSnapshotAsync(connection, requestId, ct)
            ?? throw new NotFoundException("Request not found.", ErrorCodes.NOT_FOUND);
        var steps = await _requests.GetStepsAsync(connection, requestId, ct);
        var actors = await _requests.GetActiveActorsAsync(connection, requestId, ct);
        return new RequestAccess(request, steps, actors);
    }

    // Upload and removal: the request is in progress and the person is its requester, an Admin,
    // or someone the request currently offers an action to. Management and the SystemAdmin only read.
    private static bool CanChange(ActorContext actor, RequestAccess access)
    {
        if (access.Request.CurrentStatus != RequestStatus.InProgress)
        {
            return false;
        }

        var isRequester = IsRequester(actor, access.Request);
        var isAdmin = actor.Roles.Contains(Roles.Admin);
        if (!isRequester && !isAdmin && actor.Roles.Overlaps(Roles.DocumentReadOnly))
        {
            return false;
        }
        if (isRequester || isAdmin)
        {
            return true;
        }

        var current = access.Steps.FirstOrDefault(s => s.Seq == access.Request.CurrentStepSeq);
        var offered = AllowedActionsCalculator.Compute(
            actor,
            access.Request.CurrentStatus,
            access.Request.RequesterEmployeeId,
            access.Request.CurrentStepSeq,
            current?.StepType,
            access.Actors,
            false,
            current?.State == StepState.Pending);
        return offered.Count > 0;
    }

    // Removal is further limited to the uploader and the Admin.
    private static bool CanRemove(ActorContext actor, DocumentRow row) =>
        actor.Roles.Contains(Roles.Admin) || row.UploadedByUserId == actor.UserId;

    private static bool IsRequester(ActorContext actor, RequestSnapshot request) =>
        actor.EmployeeId is { } id && id == request.RequesterEmployeeId;

    private (string Name, string Extension) ValidateFile(DocumentUpload upload)
    {
        var name = CleanName(upload.FileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var contentType = (upload.ContentType ?? string.Empty).Split(';')[0].Trim();

        if (name.Length == 0 || !ContentTypes.TryGetValue(extension, out var expected) ||
            !(NeutralContentTypes.Contains(contentType) || string.Equals(contentType, expected, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException("file",
                "This file type is not allowed. Upload a PDF, an image or an Office document.",
                ErrorCodes.DOCUMENT_TYPE_NOT_ALLOWED);
        }
        if (upload.Length <= 0)
        {
            throw new ValidationException("file", "The file is empty.");
        }
        if (upload.Length > _settings.MaxBytes)
        {
            throw new ValidationException("file",
                $"The file is larger than {FormatLimit(_settings.MaxBytes)}.", ErrorCodes.DOCUMENT_TOO_LARGE);
        }
        return (name, extension);
    }

    // The name is for display only: any folder part is dropped and control characters are removed.
    private static string CleanName(string? fileName)
    {
        var name = fileName ?? string.Empty;
        var cut = name.LastIndexOfAny(new[] { '/', '\\' });
        if (cut >= 0)
        {
            name = name[(cut + 1)..];
        }
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > NameLimit)
        {
            var extension = Path.GetExtension(name);
            name = name[..(NameLimit - extension.Length)] + extension;
        }
        return name;
    }

    private async Task<string?> ValidateStepAsync(RequestSnapshot request, string? stepKey, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(stepKey) ? null : stepKey.Trim();
        if (key is null)
        {
            return null;
        }
        var issued = await _definitions.GetByIdAsync(request.DefinitionId, ct)
            ?? throw new InvalidOperationException($"Definition {request.DefinitionId} of request {request.Id} is missing.");
        if (!issued.Definition.Steps.Any(s => s.Key == key))
        {
            throw new ValidationException("stepKey", "This is not a step of the request.");
        }
        return key;
    }

    private static string FormatLimit(long bytes) =>
        bytes % (1024 * 1024) == 0 ? $"{bytes / (1024 * 1024)} MB" : $"{bytes / 1024} KB";

    private static string? RoleLabel(ActorContext actor, RequestAccess access)
    {
        if (actor.Roles.Contains(Roles.Admin))
        {
            return Roles.Admin;
        }
        if (IsRequester(actor, access.Request))
        {
            return Labels.Requester;
        }
        var held = access.Actors.FirstOrDefault(a =>
            (a.RoleName is { } role && actor.Roles.Contains(role)) || (a.EmployeeId is { } id && actor.EmployeeId == id));
        if (held is not null)
        {
            return held.RoleName ?? Labels.ReportingManager;
        }
        return actor.Roles.FirstOrDefault(r => r != Roles.Employee);
    }

    private static DocumentDto ToDto(DocumentRow row, bool canRemove) =>
        new(row.Id, row.OriginalName, row.SizeBytes, row.ContentType, row.StepKey, row.UploadedByName, row.UploadedUtc, canRemove);

    private static ForbiddenException NotAllowed() =>
        new("You cannot perform this action on this request.", ErrorCodes.ACTION_NOT_ALLOWED);

    private Task AppendAuditAsync(
        System.Data.Common.DbTransaction tx, long requestId, string eventType, ActorContext actor, string? role,
        string? stepKey, string fileName, long sizeBytes, DateTime now, CancellationToken ct)
    {
        var details = JsonSerializer.Serialize(new { fileName, sizeBytes });
        return _audit.AppendAsync(
            tx,
            new AuditEvent(requestId, eventType, actor.UserId, actor.Name, role, stepKey, null, null, null, details,
                _correlation.CorrelationId, now),
            ct);
    }
}
