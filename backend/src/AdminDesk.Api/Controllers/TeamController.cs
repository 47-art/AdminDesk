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
    private readonly IMasterService _masters;

    public TeamController(IMasterService masters)
    {
        _masters = masters;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<TeamMemberRow>>>> List([FromQuery] TeamQuery query, CancellationToken ct)
    {
        var page = await _masters.ListTeamAsync(User.EmployeeId(), User.RoleNames(), query.Q, query.Page, query.PageSize, ct);
        return Ok(ApiResponse<PagedResult<TeamMemberRow>>.Ok(page));
    }
}
