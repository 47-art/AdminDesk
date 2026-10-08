using AdminDesk.Api.Auth;
using AdminDesk.Application.Masters;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

public sealed class MasterListParams
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? Search { get; set; }

    public string? Status { get; set; }

    public long? Holder { get; set; }

    // Also list retired records; only the roles that edit the master may ask for this.
    public bool IncludeRetired { get; set; }
}

public sealed class MasterListParamsValidator : AbstractValidator<MasterListParams>
{
    public MasterListParamsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MasterService.MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(30);
    }
}

// The SIM, asset and ID card masters: lists with history for the viewing roles, and add, edit and retire
// for each master's owning roles. Rules live in the service; holder and status are never accepted here.
[ApiController]
[Route(ApiRoutes.Masters)]
public class MastersController : ControllerBase
{
    private readonly IMasterAssetService _masters;
    private readonly ClaimsActorContextFactory _actors;

    public MastersController(IMasterAssetService masters, ClaimsActorContextFactory actors)
    {
        _masters = masters;
        _actors = actors;
    }

    // ------------------------------------------------------------------ SIMs

    [HttpGet(MasterTypes.SimRoute)]
    [Authorize(Policy = Policies.SimMasterViewers)]
    public async Task<ActionResult<ApiResponse<PagedResult<SimDto>>>> ListSims([FromQuery] MasterListParams q, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<SimDto>>.Ok(await _masters.ListSimsAsync(q.Page, q.PageSize, q.Search, q.Status, q.Holder, ct, q.IncludeRetired, User.RoleNames())));

    [HttpGet(MasterTypes.SimRoute + "/{id:long}/history")]
    [Authorize(Policy = Policies.SimMasterViewers)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterHistoryDto>>>> SimHistory(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<MasterHistoryDto>>.Ok(await _masters.GetHistoryAsync(MasterTypes.Sim, id, ct)));

    [HttpPost(MasterTypes.SimRoute)]
    [Authorize(Policy = Policies.SimMasterEditors)]
    public async Task<ActionResult<ApiResponse<SimDto>>> AddSim([FromBody] SimFieldsBody body, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created,
            ApiResponse<SimDto>.Ok(await _masters.AddSimAsync(_actors.Create(User), body, ct)));

    [HttpPut(MasterTypes.SimRoute + "/{id:long}")]
    [Authorize(Policy = Policies.SimMasterEditors)]
    public async Task<ActionResult<ApiResponse<SimDto>>> EditSim(long id, [FromBody] SimFieldsBody body, CancellationToken ct) =>
        Ok(ApiResponse<SimDto>.Ok(await _masters.EditSimAsync(_actors.Create(User), id, body, ct)));

    [HttpPost(MasterTypes.SimRoute + "/{id:long}/retire")]
    [Authorize(Policy = Policies.SimMasterEditors)]
    public async Task<ActionResult<ApiResponse>> RetireSim(long id, CancellationToken ct)
    {
        await _masters.RetireAsync(_actors.Create(User), MasterTypes.Sim, id, ct);
        return Ok(ApiResponse.Ok());
    }

    // ---------------------------------------------------------------- assets

    [HttpGet(MasterTypes.AssetRoute)]
    [Authorize(Policy = Policies.AssetMasterViewers)]
    public async Task<ActionResult<ApiResponse<PagedResult<AssetDto>>>> ListAssets([FromQuery] MasterListParams q, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<AssetDto>>.Ok(await _masters.ListAssetsAsync(q.Page, q.PageSize, q.Search, q.Status, q.Holder, ct, q.IncludeRetired, User.RoleNames())));

    [HttpGet(MasterTypes.AssetRoute + "/{id:long}/history")]
    [Authorize(Policy = Policies.AssetMasterViewers)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterHistoryDto>>>> AssetHistory(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<MasterHistoryDto>>.Ok(await _masters.GetHistoryAsync(MasterTypes.Asset, id, ct)));

    [HttpPost(MasterTypes.AssetRoute)]
    [Authorize(Policy = Policies.AssetMasterEditors)]
    public async Task<ActionResult<ApiResponse<AssetDto>>> AddAsset([FromBody] AssetFieldsBody body, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created,
            ApiResponse<AssetDto>.Ok(await _masters.AddAssetAsync(_actors.Create(User), body, ct)));

    [HttpPut(MasterTypes.AssetRoute + "/{id:long}")]
    [Authorize(Policy = Policies.AssetMasterEditors)]
    public async Task<ActionResult<ApiResponse<AssetDto>>> EditAsset(long id, [FromBody] AssetFieldsBody body, CancellationToken ct) =>
        Ok(ApiResponse<AssetDto>.Ok(await _masters.EditAssetAsync(_actors.Create(User), id, body, ct)));

    [HttpPost(MasterTypes.AssetRoute + "/{id:long}/retire")]
    [Authorize(Policy = Policies.AssetMasterEditors)]
    public async Task<ActionResult<ApiResponse>> RetireAsset(long id, CancellationToken ct)
    {
        await _masters.RetireAsync(_actors.Create(User), MasterTypes.Asset, id, ct);
        return Ok(ApiResponse.Ok());
    }

    // -------------------------------------------------------------- ID cards

    [HttpGet(MasterTypes.IdCardRoute)]
    [Authorize(Policy = Policies.IdCardMasterViewers)]
    public async Task<ActionResult<ApiResponse<PagedResult<IdCardDto>>>> ListIdCards([FromQuery] MasterListParams q, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<IdCardDto>>.Ok(await _masters.ListIdCardsAsync(q.Page, q.PageSize, q.Search, q.Status, q.Holder, ct, q.IncludeRetired, User.RoleNames())));

    [HttpGet(MasterTypes.IdCardRoute + "/{id:long}/history")]
    [Authorize(Policy = Policies.IdCardMasterViewers)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MasterHistoryDto>>>> IdCardHistory(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<MasterHistoryDto>>.Ok(await _masters.GetHistoryAsync(MasterTypes.IdCard, id, ct)));

    [HttpPost(MasterTypes.IdCardRoute)]
    [Authorize(Policy = Policies.IdCardMasterEditors)]
    public async Task<ActionResult<ApiResponse<IdCardDto>>> AddIdCard([FromBody] IdCardAddBody body, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created,
            ApiResponse<IdCardDto>.Ok(await _masters.AddIdCardAsync(_actors.Create(User), body, ct)));

    [HttpPut(MasterTypes.IdCardRoute + "/{id:long}")]
    [Authorize(Policy = Policies.IdCardMasterEditors)]
    public async Task<ActionResult<ApiResponse<IdCardDto>>> EditIdCard(long id, [FromBody] IdCardEditBody body, CancellationToken ct) =>
        Ok(ApiResponse<IdCardDto>.Ok(await _masters.EditIdCardAsync(_actors.Create(User), id, body, ct)));

    [HttpPost(MasterTypes.IdCardRoute + "/{id:long}/retire")]
    [Authorize(Policy = Policies.IdCardMasterEditors)]
    public async Task<ActionResult<ApiResponse>> RetireIdCard(long id, CancellationToken ct)
    {
        await _masters.RetireAsync(_actors.Create(User), MasterTypes.IdCard, id, ct);
        return Ok(ApiResponse.Ok());
    }
}
