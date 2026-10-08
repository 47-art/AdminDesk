using System.Text.Json;
using AdminDesk.Application.Jobs;
using Hangfire;

namespace AdminDesk.Infrastructure.Jobs;

// Scheduler backed by Hangfire. Jobs are stored in hangfire.db, a file separate from
// the application database.
public sealed class HangfireJobScheduler : IJobScheduler
{
    private readonly IRecurringJobManager _recurring;
    private readonly IBackgroundJobClient _client;

    public HangfireJobScheduler(IRecurringJobManager recurring, IBackgroundJobClient client)
    {
        _recurring = recurring;
        _client = client;
    }

    public void AddOrUpdateRecurring<THandler>(string id, string cron) where THandler : IJobHandler =>
        _recurring.AddOrUpdate<HandlerInvoker<THandler>>(id, x => x.RunAsync(null, CancellationToken.None), cron);

    public string Schedule<THandler>(TimeSpan delay, object? args = null) where THandler : IJobHandler
    {
        var json = Serialize(args);
        return _client.Schedule<HandlerInvoker<THandler>>(x => x.RunAsync(json, CancellationToken.None), delay);
    }

    public string Enqueue<THandler>(object? args = null) where THandler : IJobHandler
    {
        var json = Serialize(args);
        return _client.Enqueue<HandlerInvoker<THandler>>(x => x.RunAsync(json, CancellationToken.None));
    }

    internal static string? Serialize(object? args) => args is null ? null : JsonSerializer.Serialize(args);
}
