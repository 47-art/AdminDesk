using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AdminDesk.Application.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Jobs;

// Fallback scheduler that needs no extra package. Recurring jobs live in memory and
// are registered again at every start. Delayed and enqueued jobs are rows in
// scheduled_jobs, polled every few seconds, so they survive a restart.
public sealed partial class TimerJobScheduler : IJobScheduler, IHostedService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private const int MaxClaimPerPoll = 20;
    private const string SupportedForms = "'*/N * * * * *' (every N seconds) or '*/N * * * *' (every N minutes)";

    private readonly ScheduledJobRepository _repository;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<TimerJobScheduler> _logger;
    private readonly ConcurrentDictionary<string, RecurringEntry> _recurring = new();
    private CancellationTokenSource? _stopping;
    private Task? _loop;

    public TimerJobScheduler(
        ScheduledJobRepository repository,
        IServiceScopeFactory scopes,
        TimeProvider clock,
        ILogger<TimerJobScheduler> logger)
    {
        _repository = repository;
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
    }

    public void AddOrUpdateRecurring<THandler>(string id, string cron) where THandler : IJobHandler
    {
        var interval = ParseCron(cron);
        var entry = new RecurringEntry(
            interval,
            (jobId, ct) => new HandlerInvoker<THandler>(_scopes).RunAsync(jobId, null, ct),
            _clock.GetUtcNow().UtcDateTime + interval);
        _recurring[id] = entry;
    }

    public string Schedule<THandler>(TimeSpan delay, object? args = null) where THandler : IJobHandler
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return Insert(typeof(THandler), args, now + delay, now);
    }

    public string Enqueue<THandler>(object? args = null) where THandler : IJobHandler
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return Insert(typeof(THandler), args, now, now);
    }

    private string Insert(Type handler, object? args, DateTime due, DateTime now)
    {
        var id = Guid.NewGuid().ToString("N");
        var json = HangfireJobScheduler.Serialize(args);
        _repository.InsertAsync(id, handler.AssemblyQualifiedName!, json, due, now, CancellationToken.None)
            .GetAwaiter().GetResult();
        return id;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var reset = await _repository.ResetRunningAsync(cancellationToken);
        if (reset > 0)
        {
            _logger.LogWarning("{Count} jobs were interrupted by the last stop and will run again", reset);
        }
        _stopping = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_stopping.Token));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null || _loop is null)
        {
            return;
        }
        await _stopping.CancelAsync();
        try
        {
            await _loop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var nextPoll = DateTime.MinValue;
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                FireRecurring(now, ct);
                if (now >= nextPoll)
                {
                    nextPoll = now + PollInterval;
                    await PollAsync(now, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void FireRecurring(DateTime now, CancellationToken ct)
    {
        foreach (var (id, entry) in _recurring)
        {
            if (now < entry.NextDue)
            {
                continue;
            }
            entry.NextDue = now + entry.Interval;
            if (Interlocked.Exchange(ref entry.Running, 1) == 1)
            {
                continue;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await entry.Run(id, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recurring job {JobId} failed", id);
                }
                finally
                {
                    Interlocked.Exchange(ref entry.Running, 0);
                }
            }, CancellationToken.None);
        }
    }

    private async Task PollAsync(DateTime now, CancellationToken ct)
    {
        IReadOnlyList<ScheduledJobRow> due;
        try
        {
            due = await _repository.ClaimDueAsync(now, MaxClaimPerPoll, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Reading due jobs failed");
            return;
        }

        foreach (var row in due)
        {
            _ = Task.Run(() => RunRowAsync(row, ct), CancellationToken.None);
        }
    }

    private async Task RunRowAsync(ScheduledJobRow row, CancellationToken ct)
    {
        string state;
        string? error = null;
        try
        {
            var type = Type.GetType(row.HandlerType, throwOnError: true)!;
            var invokerType = typeof(HandlerInvoker<>).MakeGenericType(type);
            var invoker = Activator.CreateInstance(invokerType, _scopes)!;
            var run = invokerType.GetMethod("RunAsync", new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
            await (Task)run.Invoke(invoker, new object?[] { row.Id, row.ArgsJson, ct })!;
            state = ScheduledJobRepository.Completed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Left as running; the next start puts it back to pending.
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled job {JobId} failed", row.Id);
            state = ScheduledJobRepository.Failed;
            error = ex.Message;
        }

        try
        {
            await _repository.FinishAsync(row.Id, state, error, _clock.GetUtcNow().UtcDateTime, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording the result of job {JobId} failed", row.Id);
        }
    }

    private static TimeSpan ParseCron(string cron)
    {
        var match = SecondsForm().Match(cron);
        if (match.Success && int.Parse(match.Groups[1].Value) > 0)
        {
            return TimeSpan.FromSeconds(int.Parse(match.Groups[1].Value));
        }
        match = MinutesForm().Match(cron);
        if (match.Success && int.Parse(match.Groups[1].Value) > 0)
        {
            return TimeSpan.FromMinutes(int.Parse(match.Groups[1].Value));
        }
        throw new NotSupportedException($"The timer scheduler does not support the cron text '{cron}'. Supported forms: {SupportedForms}.");
    }

    [GeneratedRegex(@"^\*/(\d+) \* \* \* \* \*$")]
    private static partial Regex SecondsForm();

    [GeneratedRegex(@"^\*/(\d+) \* \* \* \*$")]
    private static partial Regex MinutesForm();

    private sealed class RecurringEntry
    {
        public RecurringEntry(TimeSpan interval, Func<string, CancellationToken, Task> run, DateTime nextDue)
        {
            Interval = interval;
            Run = run;
            NextDue = nextDue;
        }

        public TimeSpan Interval { get; }
        public Func<string, CancellationToken, Task> Run { get; }
        public DateTime NextDue { get; set; }
        public int Running;
    }
}
