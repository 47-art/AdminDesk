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
    public const string StepActivated = "StepActivated";
    public const string StepSkipped = "StepSkipped";
    public const string DefinitionSynced = "DefinitionSynced";
    public const string DocumentUploaded = "DocumentUploaded";
    public const string DocumentDownloaded = "DocumentDownloaded";
    public const string DocumentRemoved = "DocumentRemoved";
    public const string LimitChanged = "LimitChanged";
    public const string ConditionChanged = "ConditionChanged";

    // A request flow changed a master record (allocation, return, replacement).
    public const string MasterUpdated = "MasterUpdated";

    // An owner added, edited or retired a master record.
    public const string MasterRecordChanged = "MasterRecordChanged";
}
