using AdminDesk.Api.Auth;
using AdminDesk.Application.Requests;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.Authenticated)]
[Route(ApiRoutes.Dashboard)]
public class DashboardController : ControllerBase
{
    private readonly IRequestQueryService _queries;
    private readonly ClaimsActorContextFactory _actors;

    public DashboardController(IRequestQueryService queries, ClaimsActorContextFactory actors)
    {
        _queries = queries;
        _actors = actors;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<DashboardSummary>>> Summary(CancellationToken ct) =>
        Ok(ApiResponse<DashboardSummary>.Ok(await _queries.SummaryAsync(_actors.Create(User), ct)));
}
