using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;

namespace AdminDesk.Api.Diagnostics;

// Test-only trigger for the logging checks. It is mapped only when both the log
// diagnostics flag and demo mode are on, and it is not part of the product API.
public sealed class LogDiagnosticsPipelineModule : IPipelineModule
{
    public int Order => 900;

    public void MapEndpoints(WebApplication app)
    {
        var enabled = app.Configuration.GetValue<bool>(ConfigKeys.DiagnosticsEnableLogTest)
            && app.Configuration.GetValue<bool>(ConfigKeys.DemoEnabled);
        if (!enabled)
        {
            return;
        }

        app.MapGet(ApiRoutes.LogTest, [AllowAnonymous] (string? mode, ILoggerFactory loggers) =>
        {
            var logger = loggers.CreateLogger("AdminDesk.Api.Diagnostics.LogTest");
            switch (mode)
            {
                case "error":
                    throw new InvalidOperationException("Forced diagnostic error");
                case "warning":
                    logger.LogWarning("Forced diagnostic warning");
                    break;
                default:
                    logger.LogInformation("Forced diagnostic information");
                    break;
            }
            return Results.Ok(ApiResponse.Ok());
        });
    }
}
