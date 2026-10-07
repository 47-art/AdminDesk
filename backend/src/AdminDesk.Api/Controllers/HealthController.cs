using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using Dapper;
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
        await using var connection = await _connections.OpenAsync(ct);
        var scripts = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM SchemaVersions", cancellationToken: ct));
        return Ok(ApiResponse<HealthStatus>.Ok(new HealthStatus("ok", DateTime.UtcNow, scripts)));
    }
}

public record HealthStatus(string Status, DateTime UtcNow, int SchemaScripts);
