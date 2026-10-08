using AdminDesk.Application.Abstractions.Persistence;
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
    private readonly IDbConnectionFactory _connections;

    public HealthController(IDbConnectionFactory connections)
    {
        _connections = connections;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<HealthStatus>>> Get(CancellationToken ct)
    {
        // Opening the connection proves the database is reachable; nothing about it is reported.
        await using var connection = await _connections.OpenAsync(ct);
        return Ok(ApiResponse<HealthStatus>.Ok(new HealthStatus("ok")));
    }
}

public record HealthStatus(string Status);
