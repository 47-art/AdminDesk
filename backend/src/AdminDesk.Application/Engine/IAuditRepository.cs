using System.Data.Common;

namespace AdminDesk.Application.Engine;

public sealed record AuditEvent(
    long? RequestId,
    string EventType,
    string? ActorUserId,
    string ActorName,
    string? ActorRole,
    string? StepKey,
    string? FromStatus,
    string? ToStatus,
    string? Comment,
    string? DetailsJson,
    string? CorrelationId,
    DateTime CreatedUtc);

// The audit trail can only grow.
public interface IAuditRepository
{
    Task AppendAsync(DbTransaction tx, AuditEvent auditEvent, CancellationToken ct);
}
