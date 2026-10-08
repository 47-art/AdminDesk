using AdminDesk.SharedKernel.Constants;

namespace AdminDesk.Infrastructure.Persistence;

// SQL fragments for the standard stamp columns of business tables. Business rows are
// never removed; a delete is SoftDeleteSet.
public static class AuditSql
{
    // Audit events that record somebody acting on a request; creating, system and document read events do not.
    public static readonly string[] ActionEventTypes =
    {
        AuditEventTypes.StepApproved, AuditEventTypes.StepCompleted, AuditEventTypes.Rejected,
        AuditEventTypes.Cancelled, AuditEventTypes.Closed
    };

    public static string Active(string alias) => AuditColumns.ActiveFilter(alias);

    public const string InsertColumns =
        $"{AuditColumns.CreatedUtc}, {AuditColumns.CreatedBy}, {AuditColumns.UpdatedUtc}, {AuditColumns.UpdatedBy}";

    // is_active takes its default of 1 and deleted_utc stays NULL.
    public const string InsertValues = "@CreatedUtc, @CreatedBy, @UpdatedUtc, @UpdatedBy";

    public const string UpdateSet =
        $"{AuditColumns.UpdatedUtc} = @UpdatedUtc, {AuditColumns.UpdatedBy} = @UpdatedBy";

    public const string SoftDeleteSet =
        $"{AuditColumns.IsActive} = 0, {AuditColumns.DeletedUtc} = @UpdatedUtc, " +
        $"{AuditColumns.UpdatedUtc} = @UpdatedUtc, {AuditColumns.UpdatedBy} = @UpdatedBy";
}
