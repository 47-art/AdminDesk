using AdminDesk.Api.Auth;
using AdminDesk.Application.Masters;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

public sealed class TeamQuery
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? Q { get; set; }
}

public sealed class TeamQueryValidator : AbstractValidator<TeamQuery>
{
    public TeamQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MasterService.MaxPageSize);
        RuleFor(x => x.Q).MaximumLength(100);
    }
}

[ApiController]
[Authorize(Policy = Policies.TeamViewers)]
[Route(ApiRoutes.Team)]
public class TeamController : ControllerBase
{
    private static readonly string[] SeeEveryone = { Roles.Admin, Roles.HR, Roles.Management, Roles.SystemAdmin };

    private readonly IMasterService _masters;

    public TeamController(IMasterService masters)
    {
        _masters = masters;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<TeamMemberRow>>>> List([FromQuery] TeamQuery query, CancellationToken ct)
    {
        var roles = User.RoleNames();
        var employeeId = User.EmployeeId();

        // Someone without an employee record who cannot see everyone has no team.
        if (employeeId is null && !roles.Any(r => SeeEveryone.Contains(r)))
        {
            return Ok(ApiResponse<PagedResult<TeamMemberRow>>.Ok(
                new PagedResult<TeamMemberRow>(Array.Empty<TeamMemberRow>(), 0, query.Page, query.PageSize)));
        }

        var page = await _masters.ListTeamAsync(employeeId ?? 0, roles, query.Q, query.Page, query.PageSize, ct);
        return Ok(ApiResponse<PagedResult<TeamMemberRow>>.Ok(page));
    }
}
