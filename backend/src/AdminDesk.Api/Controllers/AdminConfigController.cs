using AdminDesk.Api.Auth;
using AdminDesk.Application.Definitions;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

public sealed class UpdateLimitBodyValidator : AbstractValidator<UpdateLimitBody>
{
    public UpdateLimitBodyValidator()
    {
        RuleFor(x => x.StepKey).NotEmpty().WithMessage("Choose a step.").MaximumLength(80);
        RuleFor(x => x.LimitKey).NotEmpty().WithMessage("Choose a limit.").MaximumLength(80);
        RuleFor(x => x.ValueMinor).GreaterThanOrEqualTo(0).WithMessage("A limit cannot be negative.");
    }
}

public sealed class SaveConditionBodyValidator : AbstractValidator<SaveConditionBody>
{
    public SaveConditionBodyValidator()
    {
        RuleFor(x => x.BaseVersion).GreaterThan(0).WithMessage("Reload the page and try again.");

        When(x => x.Condition is not null, () =>
        {
            RuleFor(x => x.Condition!.Field).NotEmpty().WithMessage("Choose a field.").MaximumLength(160)
                .OverridePropertyName("condition.field");
            RuleFor(x => x.Condition!.Op).NotEmpty().WithMessage("Choose an operator.").MaximumLength(20)
                .OverridePropertyName("condition.op");
            RuleFor(x => x.Condition!.Limit).MaximumLength(80)
                .OverridePropertyName("condition.limit");
        });
    }
}

// Limits and step conditions of every module. Reading is open to the viewers; changing is for the limit editors.
[ApiController]
[Route(ApiRoutes.Admin + "/modules")]
public class AdminConfigController : ControllerBase
{
    private readonly IModuleConfigService _config;
    private readonly ClaimsActorContextFactory _actors;

    public AdminConfigController(IModuleConfigService config, ClaimsActorContextFactory actors)
    {
        _config = config;
        _actors = actors;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ConfigViewers)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ModuleConfigDto>>>> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ModuleConfigDto>>.Ok(await _config.GetAllAsync(ct)));

    [HttpGet("{code}")]
    [Authorize(Policy = Policies.ConfigViewers)]
    public async Task<ActionResult<ApiResponse<ModuleConfigDto>>> Get(string code, CancellationToken ct) =>
        Ok(ApiResponse<ModuleConfigDto>.Ok(await _config.GetAsync(code, ct)));

    [HttpPut("{code}/limits")]
    [Authorize(Policy = Policies.LimitEditors)]
    public async Task<ActionResult<ApiResponse<ModuleConfigDto>>> UpdateLimit(
        string code, [FromBody] UpdateLimitBody body, CancellationToken ct)
    {
        await _config.UpdateLimitAsync(_actors.Create(User), code, body.StepKey, body.LimitKey, body.ValueMinor, ct);
        return Ok(ApiResponse<ModuleConfigDto>.Ok(await _config.GetAsync(code, ct)));
    }

    [HttpPut("{code}/steps/{stepKey}/condition")]
    [Authorize(Policy = Policies.LimitEditors)]
    public async Task<ActionResult<ApiResponse<ModuleConfigDto>>> SaveCondition(
        string code, string stepKey, [FromBody] SaveConditionBody body, CancellationToken ct)
    {
        await _config.SaveConditionAsync(_actors.Create(User), code, stepKey, body.BaseVersion, body.Condition, ct);
        return Ok(ApiResponse<ModuleConfigDto>.Ok(await _config.GetAsync(code, ct)));
    }
}
