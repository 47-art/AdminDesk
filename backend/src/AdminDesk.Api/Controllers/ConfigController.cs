using AdminDesk.Application.Auth;
using AdminDesk.Application.Demo;
using AdminDesk.Application.Masters;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

[ApiController]
[Route(ApiRoutes.Config)]
public class ConfigController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IEmployeeRepository _employees;

    public ConfigController(IConfiguration configuration, IEmployeeRepository employees)
    {
        _configuration = configuration;
        _employees = employees;
    }

    [HttpGet("public")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PublicConfigDto>>> GetPublic(CancellationToken ct)
    {
        var demo = bool.TryParse(_configuration[ConfigKeys.DemoEnabled], out var enabled) && enabled;
        if (!demo)
        {
            return Ok(ApiResponse<PublicConfigDto>.Ok(new PublicConfigDto(false, null, Array.Empty<DemoAccountDto>())));
        }

        // Only people that exist in the organisation get a card.
        var accounts = new List<DemoAccountDto>();
        foreach (var account in DemoAccountCatalog.Accounts)
        {
            if (await _employees.GetByCodeAsync(account.EmployeeCode, ct) is not null)
            {
                accounts.Add(new DemoAccountDto(account.Role, account.Name, account.Email, account.Description));
            }
        }

        return Ok(ApiResponse<PublicConfigDto>.Ok(new PublicConfigDto(true, ConfigKeys.DemoPassword, accounts)));
    }
}
