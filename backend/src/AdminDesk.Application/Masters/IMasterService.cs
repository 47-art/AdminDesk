namespace AdminDesk.Application.Masters;

public interface IMasterService
{
    // actorEmployeeId is the signed-in person's employee id (null when they have none); only the held-item
    // kinds use it.
    Task<IReadOnlyList<LookupItem>> SearchLookupAsync(string kind, string? q, int take, long? actorEmployeeId, CancellationToken ct);

    Task<LookupItem> GetLookupAsync(string kind, long id, CancellationToken ct);

    // A caller holding only the Manager role sees direct reports; broader roles see everyone.
    Task<PagedResult<TeamMemberRow>> ListTeamAsync(
        long? callerEmployeeId, IReadOnlyCollection<string> callerRoles, string? q, int page, int pageSize, CancellationToken ct);
}
