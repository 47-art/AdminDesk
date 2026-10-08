using AdminDesk.Application.Engine;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Application.Requests;

// Row classes use settable properties so the database driver's 64-bit integers bind cleanly.
public sealed class RequestHeaderRow
{
    public long Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public long DefinitionId { get; set; }
    public long RequesterEmployeeId { get; set; }
    public string RequesterCode { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public long? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public long? LocationId { get; set; }
    public string? LocationName { get; set; }
    public long? CostCentreId { get; set; }
    public string? CostCentreName { get; set; }
    public DateOnly RequestDate { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string? CurrentStepKey { get; set; }
    public long? CurrentStepSeq { get; set; }
    public string? ResponsibleName { get; set; }
    public string? ResponsibleRole { get; set; }
    public string? Remarks { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public long RowVersion { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ClosedUtc { get; set; }
}

public sealed class CancelEventRow
{
    public string? Comment { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class RequestDetailRows
{
    public RequestHeaderRow Header { get; set; } = new();
    public IReadOnlyList<RequestStepRow> Steps { get; set; } = Array.Empty<RequestStepRow>();
    public IReadOnlyList<ActorRow> ActiveActors { get; set; } = Array.Empty<ActorRow>();
    public CancelEventRow? Cancelled { get; set; }
}

public sealed class RequestListRow
{
    public long Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public long DefinitionId { get; set; }
    public string? Subject { get; set; }
    public DateOnly RequestDate { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string? CurrentStepKey { get; set; }
    public string? CurrentStepName { get; set; }
    public string? ResponsibleName { get; set; }
    public string? ResponsibleRole { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public string? RequesterDepartment { get; set; }
}

public sealed class AuditRow
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string? ActorRole { get; set; }
    public string? StepKey { get; set; }
    public string? StepName { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class SummaryCounts
{
    public long Total { get; set; }
    public long Pending { get; set; }
    public long Approved { get; set; }
    public long Rejected { get; set; }
    public long Completed { get; set; }
    public long Cancelled { get; set; }
}

public sealed record PagedRows<T>(IReadOnlyList<T> Items, int Total);

// Parsed, validated filters. Sort and direction are still the caller's text: the repository
// only ever looks them up in a fixed list.
public sealed record MineFilter(
    string? Q,
    IReadOnlyList<RequestStatus> Statuses,
    ApprovalStatus? ApprovalStatus,
    string? Module,
    DateOnly? From,
    DateOnly? To,
    int Page,
    int PageSize,
    string? Sort,
    string? Dir);

public sealed record InboxFilter(string? Module, string? Requester, Priority? Priority, int Page, int PageSize);

// Read side of requests. Nothing here writes.
public interface IRequestQueryRepository
{
    Task<bool> ExistsAsync(long id, CancellationToken ct);

    // Null when the request does not exist or is inactive.
    Task<RequestDetailRows?> GetDetailAsync(long id, CancellationToken ct);

    Task<PagedRows<RequestListRow>> ListMineAsync(long employeeId, MineFilter filter, CancellationToken ct);

    // employeeId is null for a user without an employee record: only the role match applies.
    Task<PagedRows<RequestListRow>> ListInboxAsync(
        long? employeeId, IReadOnlyCollection<string> roles, InboxFilter filter, CancellationToken ct);

    Task<int> CountInboxAsync(long? employeeId, IReadOnlyCollection<string> roles, CancellationToken ct);

    // employeeId is null for organisation-wide counters (no requester filter).
    Task<SummaryCounts> SummaryAsync(long? employeeId, CancellationToken ct);

    // The latest requests the user raised or acted on.
    Task<IReadOnlyList<RequestListRow>> ListRecentAsync(long employeeId, string userId, int take, CancellationToken ct);

    // Oldest first.
    Task<IReadOnlyList<AuditRow>> ListAuditAsync(long requestId, CancellationToken ct);
}
