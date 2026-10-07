namespace AdminDesk.SharedKernel.Constants;

public static class AuditColumns
{
    public const string CreatedUtc = "created_utc";
    public const string CreatedBy = "created_by";
    public const string UpdatedUtc = "updated_utc";
    public const string UpdatedBy = "updated_by";
    public const string IsActive = "is_active";
    public const string DeletedUtc = "deleted_utc";

    // SQL predicate selecting only live rows of the aliased table.
    public static string ActiveFilter(string alias) =>
        $"{alias}.{IsActive} = 1 AND {alias}.{DeletedUtc} IS NULL";
}
