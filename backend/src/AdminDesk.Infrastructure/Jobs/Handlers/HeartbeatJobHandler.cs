using AdminDesk.Application.Jobs;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Jobs.Handlers;

// Recurring liveness signal: one Information line per run.
public sealed class HeartbeatJobHandler : IJobHandler
{
    private readonly ILogger<HeartbeatJobHandler> _logger;
    private readonly TimeProvider _clock;

    public HeartbeatJobHandler(ILogger<HeartbeatJobHandler> logger, TimeProvider clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public Task RunAsync(JobContext context, CancellationToken ct)
    {
        _logger.LogInformation("heartbeat {Utc:O}", _clock.GetUtcNow().UtcDateTime);
        return Task.CompletedTask;
    }
}
