using AdminDesk.Application.Definitions;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Application.Requests;

public sealed record RequesterDto(long EmployeeId, string EmployeeCode, string Name, string? Department);

public sealed record LabelDto(long Id, string Label);

public sealed record ResponsibleDto(string? Name, string? Role);

public sealed record RequestStepDto(
    int Seq,
    string Key,
    string Name,
    StepType Type,
    StepState State,
    string ActorLabel,
    string? ActedByName,
    DateTime? ActedUtc,
    string? Comment,
    bool IsCurrent,
    IReadOnlyDictionary<string, object?>? Captured,
    IReadOnlyList<FieldDto> CaptureFields,
    bool RequiresDocument);

public sealed record RequestDetailDto(
    long Id,
    string RequestNo,
    string ModuleCode,
    string ModuleName,
    long DefinitionId,
    string? Subject,
    long RowVersion,
    RequesterDto Requester,
    string? Department,
    LabelDto? Project,
    LabelDto? Location,
    LabelDto? CostCentre,
    DateOnly RequestDate,
    DateOnly? RequiredDate,
    Priority Priority,
    ApprovalStatus ApprovalStatus,
    RequestStatus CurrentStatus,
    string? CurrentStepKey,
    string? CurrentStepName,
    ResponsibleDto Responsible,
    string? Remarks,
    IReadOnlyDictionary<string, object?> Payload,
    IReadOnlyDictionary<string, string> LookupLabels,
    int AgeDays,
    DateTime CreatedUtc,
    DateTime? ClosedUtc,
    ModuleDefinitionDto Definition,
    IReadOnlyList<RequestStepDto> Steps,
    IReadOnlyList<RequestAction> AllowedActions,
    string? PrimaryActionLabel,
    string? CancelReason,
    string? CancelledUtc,
    string? StoppedByName,
    string? StoppedByRole);

public sealed record RequestListItem(
    long Id,
    string RequestNo,
    string ModuleCode,
    string ModuleName,
    string? Subject,
    DateOnly RequestDate,
    DateOnly? RequiredDate,
    Priority Priority,
    RequestStatus CurrentStatus,
    string? CurrentStepName,
    StepType? CurrentStepType,
    string? ResponsibleName,
    string? ResponsibleRole,
    DateTime UpdatedUtc,
    string RequesterName,
    string? RequesterDepartment,
    int AgeDays,
    string? PrimaryActionLabel,
    IReadOnlyList<FieldDto> CaptureFields,
    bool RequiresDocument);

public sealed record AuditEventDto(
    long Id,
    string EventType,
    string ActorName,
    string? ActorRole,
    string? StepKey,
    string? StepName,
    string? FromStatus,
    string? ToStatus,
    string? Comment,
    DateTime CreatedUtc);

public sealed record DashboardSummary(
    int WaitingForMe,
    int Total,
    int Pending,
    int Approved,
    int Rejected,
    int Completed,
    int Cancelled,
    string Scope,
    IReadOnlyList<RequestListItem> Recent);

// Whose requests the dashboard counters cover.
public static class DashboardScopes
{
    public const string Organisation = "Organisation";
    public const string Mine = "Mine";
}

public sealed record InboxCountDto(int Count);
