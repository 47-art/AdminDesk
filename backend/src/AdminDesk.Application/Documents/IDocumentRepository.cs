using System.Data.Common;

namespace AdminDesk.Application.Documents;

public sealed record DocumentRow
{
    public long Id { get; init; }
    public long RequestId { get; init; }
    public string? StepKey { get; init; }
    public string OriginalName { get; init; } = string.Empty;
    public string StoredName { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string UploadedByUserId { get; init; } = string.Empty;
    public string UploadedByName { get; init; } = string.Empty;
    public DateTime UploadedUtc { get; init; }
}

public sealed record NewDocument(
    long RequestId,
    string? StepKey,
    string OriginalName,
    string StoredName,
    long SizeBytes,
    string ContentType,
    string UploadedByUserId,
    string UploadedByName,
    DateTime UploadedUtc);

// Document metadata. A removed document keeps its row; it just stops being active.
public interface IDocumentRepository
{
    Task<long> InsertAsync(DbTransaction tx, NewDocument document, CancellationToken ct);

    // Active documents of a request, oldest first.
    Task<IReadOnlyList<DocumentRow>> ListAsync(DbConnection connection, long requestId, CancellationToken ct);

    // Null when the document is unknown, belongs to another request or has been removed.
    Task<DocumentRow?> GetAsync(DbTransaction tx, long requestId, long documentId, CancellationToken ct);

    Task SoftDeleteAsync(DbTransaction tx, long documentId, string removedByName, CancellationToken ct);

    // Active documents uploaded for one step of a request.
    Task<long> CountForStepAsync(DbTransaction tx, long requestId, string stepKey, CancellationToken ct);
}

// Where the uploaded bytes live. Names are server-generated and never come from the client.
public interface IDocumentFileStore
{
    Task SaveAsync(string storedName, Stream content, CancellationToken ct);

    // Throws a not-found error when the file is missing from disk.
    Stream OpenRead(string storedName);

    // Removes a file that was written but whose metadata could not be saved.
    void DiscardUnsaved(string storedName);
}
