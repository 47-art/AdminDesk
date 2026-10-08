namespace AdminDesk.Application.Masters;

public sealed record EmployeeRecord(
    long Id,
    string Code,
    string FullName,
    string? Email,
    string? Designation,
    long? DepartmentId,
    string? DepartmentName,
    long? LocationId,
    string? LocationName,
    long? ReportingManagerId,
    string? ReportingManagerName);

public sealed record LookupItem(long Id, string Code, string Label, string? Secondary);

public sealed record TeamMemberRow(
    long EmployeeId,
    string Code,
    string Name,
    string? Designation,
    string? Department,
    string? Location);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
