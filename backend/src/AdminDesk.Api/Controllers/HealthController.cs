using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route(ApiRoutes.Health)]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<HealthStatus>> Get()
    {
        return Ok(ApiResponse<HealthStatus>.Ok(new HealthStatus("ok", DateTime.UtcNow)));
    }
}

public record HealthStatus(string Status, DateTime UtcNow);
