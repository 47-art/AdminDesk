namespace AdminDesk.Application.Documents;

public sealed record DocumentDto(
    long Id,
    string OriginalName,
    long SizeBytes,
    string ContentType,
    string? StepKey,
    string UploadedByName,
    DateTime UploadedUtc,
    bool CanRemove);

// The file as it arrives from the caller. The service owns every rule about it.
public sealed record DocumentUpload(string FileName, string? ContentType, long Length, Stream Content);

// A file ready to stream back. The caller disposes the stream.
public sealed record DocumentDownload(Stream Content, string FileName, string ContentType);

// Limits read from configuration once at start-up.
public sealed record DocumentSettings(long MaxBytes);
