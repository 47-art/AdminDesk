namespace AdminDesk.Application.Masters;

public interface IMasterService
{
    Task<IReadOnlyList<LookupItem>> SearchLookupAsync(string kind, string? q, int take, CancellationToken ct);

    Task<LookupItem> GetLookupAsync(string kind, long id, CancellationToken ct);

    // A caller holding only the Manager role sees direct reports; broader roles see everyone.
    Task<PagedResult<TeamMemberRow>> ListTeamAsync(
        long? callerEmployeeId, IReadOnlyCollection<string> callerRoles, string? q, int page, int pageSize, CancellationToken ct);
}
