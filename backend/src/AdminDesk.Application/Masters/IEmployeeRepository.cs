namespace AdminDesk.Application.Masters;

// Read access to employees and the reporting line. Only active, not removed rows
// are returned; names of related masters are shown even when those are deactivated.
public interface IEmployeeRepository
{
    Task<EmployeeRecord?> GetByIdAsync(long id, CancellationToken ct);

    Task<EmployeeRecord?> GetByCodeAsync(string code, CancellationToken ct);

    Task<EmployeeRecord?> GetByEmailAsync(string email, CancellationToken ct);

    Task<bool> HasDirectReportsAsync(long employeeId, CancellationToken ct);

    // The manager's id, or null when the employee has none or the manager is no longer active.
    Task<long?> GetReportingManagerIdAsync(long employeeId, CancellationToken ct);

    Task<int> CountAsync(CancellationToken ct);

    Task<PagedResult<TeamMemberRow>> ListDirectReportsAsync(long managerId, string? q, int page, int pageSize, CancellationToken ct);

    Task<PagedResult<TeamMemberRow>> ListAllAsync(string? q, int page, int pageSize, CancellationToken ct);

    Task<IReadOnlyList<LookupItem>> SearchLookupAsync(string q, int take, CancellationToken ct);

    // Labels for the given ids, including employees who were deactivated since, so a request keeps showing them.
    Task<IReadOnlyList<LookupItem>> GetLabelsAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
}
