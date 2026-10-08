using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;

namespace AdminDesk.Application.Masters;

public sealed class MasterService : IMasterService
{
    public const int MaxLookupTake = 50;
    public const int MaxPageSize = 100;
    public const int MinSearchLength = 2;

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
        if (text.Length < MinSearchLength)
        {
            return Array.Empty<LookupItem>();
        }
        return await provider.SearchAsync(text, Math.Clamp(take, 1, MaxLookupTake), ct);
    }

    public Task<PagedResult<TeamMemberRow>> ListTeamAsync(
        long callerEmployeeId, IReadOnlyCollection<string> callerRoles, string? q, int page, int pageSize, CancellationToken ct)
    {
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var text = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var seesEveryone = callerRoles.Any(role => SeeEveryone.Contains(role));
        return seesEveryone
            ? _employees.ListAllAsync(text, safePage, safeSize, ct)
            : _employees.ListDirectReportsAsync(callerEmployeeId, text, safePage, safeSize, ct);
    }
}
