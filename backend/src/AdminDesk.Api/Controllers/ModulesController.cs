using AdminDesk.Application.Definitions;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.Authenticated)]
[Route(ApiRoutes.Modules)]
public class ModulesController : ControllerBase
{
    private readonly IModuleCatalogService _catalogue;

    public ModulesController(IModuleCatalogService catalogue)
    {
        _catalogue = catalogue;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ModuleSummaryDto>>>> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ModuleSummaryDto>>.Ok(await _catalogue.ListAsync(ct)));

    [HttpGet("{code}")]
    public async Task<ActionResult<ApiResponse<ModuleDefinitionDto>>> Get(string code, CancellationToken ct) =>
        Ok(ApiResponse<ModuleDefinitionDto>.Ok(await _catalogue.GetAsync(code, ct)));
}
