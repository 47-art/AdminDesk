using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Jobs;
using AdminDesk.Infrastructure.Jobs.Handlers;
using AdminDesk.SharedKernel.Constants;
using Dapper;

namespace AdminDesk.Api.Jobs;

// Registers the recurring heartbeat and, when the spike flag is on, the probe jobs.
public sealed class JobsStartupTask : IStartupTask
{
    private const string SpikeRestartEnvironmentVariable = "SPIKE_RESTART_PROBE";

    private readonly IJobScheduler _scheduler;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JobsProviderInfo _provider;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobsStartupTask> _logger;

    public JobsStartupTask(
        IJobScheduler scheduler,
        IConfiguration configuration,
        IUnitOfWork unitOfWork,
        JobsProviderInfo provider,
        TimeProvider clock,
        ILogger<JobsStartupTask> logger)
    {
        _scheduler = scheduler;
        _configuration = configuration;
        _unitOfWork = unitOfWork;
        _provider = provider;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 70;

    public async Task RunAsync(CancellationToken ct)
    {
        _scheduler.AddOrUpdateRecurring<HeartbeatJobHandler>("heartbeat", "*/10 * * * * *");

        if (_configuration.GetValue<bool>(ConfigKeys.SpikeEnabled))
        {
            for (var i = 1; i <= 5; i++)
            {
                _scheduler.AddOrUpdateRecurring<SpikeWriteJobHandler>($"spike-w{i}", "*/5 * * * * *");
            }

            if (await TryMarkAsync("check2", ct))
            {
                _scheduler.Schedule<DelayedProbeJobHandler>(TimeSpan.FromSeconds(30), new DelayedProbeArgs("check2"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SpikeRestartEnvironmentVariable))
                && await TryMarkAsync("check3", ct))
            {
                _scheduler.Schedule<DelayedProbeJobHandler>(TimeSpan.FromSeconds(60), new DelayedProbeArgs("check3"));
                _logger.LogInformation("check3 scheduled");
            }
        }

        _logger.LogInformation("Jobs startup task ran (provider {Provider})", _provider.Name);
    }

    // Returns true only the first time a tag is seen in this data folder.
    private Task<bool> TryMarkAsync(string tag, CancellationToken ct) =>
        _unitOfWork.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            var existing = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM job_probe_log WHERE source = 'marker' AND worker_label = @Tag",
                new { Tag = tag }, transaction, cancellationToken: ct));
            if (existing > 0)
            {
                return false;
            }
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO job_probe_log(source, handler, worker_label, written_utc) VALUES('marker', 'startup', @Tag, @Now)",
                new { Tag = tag, Now = _clock.GetUtcNow().UtcDateTime }, transaction, cancellationToken: ct));
            return true;
        }, ct);
}
