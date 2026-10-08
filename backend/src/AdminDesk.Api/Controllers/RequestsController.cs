using AdminDesk.Api.Auth;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.Application.Requests;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

// Maps, calls the engine or the query service and wraps the envelope. The rules live in the services.
[ApiController]
[Authorize(Policy = Policies.Authenticated)]
[Route(ApiRoutes.Requests)]
public class RequestsController : ControllerBase
{
    private readonly IRequestWorkflowService _workflow;
    private readonly IRequestQueryService _queries;
    private readonly ClaimsActorContextFactory _actors;

    public RequestsController(
        IRequestWorkflowService workflow, IRequestQueryService queries, ClaimsActorContextFactory actors)
    {
        _workflow = workflow;
        _queries = queries;
        _actors = actors;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<RequestDetailDto>>> Create([FromBody] CreateRequestBody body, CancellationToken ct)
    {
        var actor = _actors.Create(User);
        var id = await _workflow.CreateAsync(actor, body.ToCommand(), ct);
        var detail = await _queries.GetDetailAsync(actor, id, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<RequestDetailDto>.Ok(detail));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<RequestDetailDto>>> Get(long id, CancellationToken ct) =>
        Ok(ApiResponse<RequestDetailDto>.Ok(await _queries.GetDetailAsync(_actors.Create(User), id, ct)));

    [HttpPost("{id:long}/actions")]
    public async Task<ActionResult<ApiResponse<RequestDetailDto>>> Act(long id, [FromBody] ActionBody body, CancellationToken ct)
    {
        var actor = _actors.Create(User);
        await _workflow.ActAsync(actor, id, body.ToCommand(), ct);
        return Ok(ApiResponse<RequestDetailDto>.Ok(await _queries.GetDetailAsync(actor, id, ct)));
    }

    [HttpGet("{id:long}/audit")]
    [Authorize(Policy = Policies.AdminOrSystemAdmin)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AuditEventDto>>>> Audit(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<AuditEventDto>>.Ok(await _queries.GetAuditAsync(_actors.Create(User), id, ct)));

    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<PagedResult<RequestListItem>>>> Mine([FromQuery] MineQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<RequestListItem>>.Ok(await _queries.MineAsync(_actors.Create(User), query, ct)));

    [HttpGet("all")]
    [Authorize(Policy = Policies.OrganisationWide)]
    public async Task<ActionResult<ApiResponse<PagedResult<RequestListItem>>>> All([FromQuery] MineQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<RequestListItem>>.Ok(await _queries.AllAsync(_actors.Create(User), query, ct)));

    [HttpGet("team")]
    public async Task<ActionResult<ApiResponse<PagedResult<RequestListItem>>>> Team([FromQuery] MineQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<RequestListItem>>.Ok(await _queries.TeamAsync(_actors.Create(User), query, ct)));

    [HttpGet("inbox")]
    public async Task<ActionResult<ApiResponse<PagedResult<RequestListItem>>>> Inbox([FromQuery] InboxQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<RequestListItem>>.Ok(await _queries.InboxAsync(_actors.Create(User), query, ct)));

    [HttpGet("inbox/count")]
    public async Task<ActionResult<ApiResponse<InboxCountDto>>> InboxCount(CancellationToken ct) =>
        Ok(ApiResponse<InboxCountDto>.Ok(new InboxCountDto(await _queries.InboxCountAsync(_actors.Create(User), ct))));
}
