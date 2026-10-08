using System.Data.Common;
using AdminDesk.Application.Engine;
using Dapper;

namespace AdminDesk.Infrastructure.Requests;

// Append-only: the table rejects updates and deletes, and this class only inserts.
public sealed class AuditRepository : IAuditRepository
{
    public async Task AppendAsync(DbTransaction tx, AuditEvent auditEvent, CancellationToken ct)
    {
        const string sql =
            "INSERT INTO audit_events (request_id, event_type, actor_user_id, actor_name, actor_role, step_key, " +
            "from_status, to_status, comment, details_json, correlation_id, created_utc) " +
            "VALUES (@RequestId, @EventType, @ActorUserId, @ActorName, @ActorRole, @StepKey, " +
            "@FromStatus, @ToStatus, @Comment, @DetailsJson, @CorrelationId, @CreatedUtc)";
        await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, auditEvent, tx, cancellationToken: ct));
    }
}
