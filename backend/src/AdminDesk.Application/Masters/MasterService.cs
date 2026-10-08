using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;

namespace AdminDesk.Application.Masters;

public sealed class MasterService : IMasterService
{
    public const int MaxLookupTake = 50;
    public const int MaxPageSize = 100;

    private static readonly string[] SeeEveryone = { Roles.Admin, Roles.HR, Roles.Management, Roles.SystemAdmin };

    private readonly ILookupRegistry _lookups;
    private readonly IEmployeeRepository _employees;

    public MasterService(ILookupRegistry lookups, IEmployeeRepository employees)
    {
        _lookups = lookups;
        _employees = employees;
    }

    public async Task<IReadOnlyList<LookupItem>> SearchLookupAsync(string kind, string? q, int take, CancellationToken ct)
    {
        var provider = _lookups.Find(kind) ?? throw new NotFoundException($"Unknown lookup '{kind}'.");
        var text = q?.Trim() ?? string.Empty;
        return await provider.SearchAsync(text, Math.Clamp(take, 1, MaxLookupTake), ct);
    }

    public async Task<LookupItem> GetLookupAsync(string kind, long id, CancellationToken ct)
    {
        var provider = _lookups.Find(kind) ?? throw new NotFoundException($"Unknown lookup '{kind}'.");
        return await provider.GetAsync(id, ct) ?? throw new NotFoundException("Lookup item not found.");
    }

    public Task<PagedResult<TeamMemberRow>> ListTeamAsync(
        long? callerEmployeeId, IReadOnlyCollection<string> callerRoles, string? q, int page, int pageSize, CancellationToken ct)
    {
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var text = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var seesEveryone = callerRoles.Any(role => SeeEveryone.Contains(role));
        if (seesEveryone)
        {
            return _employees.ListAllAsync(text, safePage, safeSize, ct);
        }

        // Someone without an employee record who cannot see everyone has no team.
        return callerEmployeeId is { } employeeId
            ? _employees.ListDirectReportsAsync(employeeId, text, safePage, safeSize, ct)
            : Task.FromResult(new PagedResult<TeamMemberRow>(Array.Empty<TeamMemberRow>(), 0, safePage, safeSize));
    }
}
