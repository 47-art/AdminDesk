namespace AdminDesk.Application.Masters;

public interface IMasterService
{
    // actorEmployeeId is the signed-in person's employee id (null when they have none). The SIM and asset
    // kinds are limited by the caller's roles: the available kinds need a viewer role for that master, the
    // held kinds list only the caller's own items (viewers may read any held item by id). Anything the
    // caller may not see answers like an item that does not exist.
    Task<IReadOnlyList<LookupItem>> SearchLookupAsync(
        string kind, string? q, int take, long? actorEmployeeId, IReadOnlyCollection<string> actorRoles, CancellationToken ct);

    Task<LookupItem> GetLookupAsync(
        string kind, long id, long? actorEmployeeId, IReadOnlyCollection<string> actorRoles, CancellationToken ct);

    // A caller holding only the Manager role sees direct reports; broader roles see everyone.
    Task<PagedResult<TeamMemberRow>> ListTeamAsync(
        long? callerEmployeeId, IReadOnlyCollection<string> callerRoles, string? q, int page, int pageSize, CancellationToken ct);
}
