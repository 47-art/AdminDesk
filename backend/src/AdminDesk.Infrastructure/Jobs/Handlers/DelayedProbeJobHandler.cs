using System.Text.Json;
using AdminDesk.Application.Jobs;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Jobs.Handlers;

public sealed record DelayedProbeArgs(string Tag);

// Logs one line when a delayed job fires, so a run can check that it fired once.
public sealed class DelayedProbeJobHandler : IJobHandler
{
    private readonly ILogger<DelayedProbeJobHandler> _logger;
    private readonly TimeProvider _clock;

    public DelayedProbeJobHandler(ILogger<DelayedProbeJobHandler> logger, TimeProvider clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public Task RunAsync(JobContext context, CancellationToken ct)
    {
        var args = context.ArgsJson is null ? null : JsonSerializer.Deserialize<DelayedProbeArgs>(context.ArgsJson);
        _logger.LogInformation("delayed-probe fired {Utc:O} {Tag}", _clock.GetUtcNow().UtcDateTime, args?.Tag ?? "none");
        return Task.CompletedTask;
    }
}
