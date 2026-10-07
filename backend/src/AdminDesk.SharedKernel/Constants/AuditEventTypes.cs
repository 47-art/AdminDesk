namespace AdminDesk.SharedKernel.Constants;

// Event types are plain strings so new ones need no schema change.
public static class AuditEventTypes
{
    public const string Created = "Created";
    public const string StepApproved = "StepApproved";
    public const string StepCompleted = "StepCompleted";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Closed = "Closed";
    public const string DefinitionSynced = "DefinitionSynced";
}
