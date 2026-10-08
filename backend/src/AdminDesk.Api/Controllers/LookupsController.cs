using AdminDesk.Api.Auth;
using AdminDesk.Application.Masters;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Responses;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

public sealed class LookupQuery
{
    public string? Q { get; set; }

    public int Take { get; set; } = 20;
}

public sealed class LookupQueryValidator : AbstractValidator<LookupQuery>
{
    public LookupQueryValidator()
    {
        RuleFor(x => x.Take).InclusiveBetween(1, MasterService.MaxLookupTake);
        RuleFor(x => x.Q).MaximumLength(100);
    }
}

[ApiController]
[Authorize(Policy = Policies.Authenticated)]
[Route(ApiRoutes.Lookups)]
public class LookupsController : ControllerBase
{
    private readonly IMasterService _masters;

    public LookupsController(IMasterService masters)
    {
        _masters = masters;
    }

    [HttpGet("{kind}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LookupItem>>>> Search(
        string kind, [FromQuery] LookupQuery query, CancellationToken ct)
    {
        var items = await _masters.SearchLookupAsync(kind, query.Q, query.Take, User.EmployeeId(), ct);
        return Ok(ApiResponse<IReadOnlyList<LookupItem>>.Ok(items));
    }

    [HttpGet("{kind}/{id:long}")]
    public async Task<ActionResult<ApiResponse<LookupItem>>> Get(string kind, long id, CancellationToken ct)
    {
        var item = await _masters.GetLookupAsync(kind, id, ct);
        return Ok(ApiResponse<LookupItem>.Ok(item));
    }
}
