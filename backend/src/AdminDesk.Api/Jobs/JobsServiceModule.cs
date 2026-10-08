using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Jobs;
using AdminDesk.Infrastructure.Configuration;
using AdminDesk.Infrastructure.Jobs;
using AdminDesk.SharedKernel.Constants;
using Hangfire;
using Hangfire.Storage.SQLite;
using SQLite;

namespace AdminDesk.Api.Jobs;

// Chooses the background job provider from configuration. Both providers sit behind
// IJobScheduler, so handlers and callers never know which one is running.
public sealed class JobsServiceModule : IServiceModule
{
    public const string HangfireProvider = "Hangfire";
    public const string TimerProvider = "Timer";
    public const string HangfireFileName = "hangfire.db";

    public int Order => 60;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration[ConfigKeys.JobsProvider];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = HangfireProvider;
        }

        foreach (var handler in typeof(HandlerInvoker<>).Assembly.GetTypes()
                     .Where(t => typeof(IJobHandler).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }))
        {
            services.AddScoped(handler);
        }

        switch (provider)
        {
            case HangfireProvider:
                ConfigureHangfire(services, configuration);
                break;
            case TimerProvider:
                services.AddSingleton<ScheduledJobRepository>();
                services.AddSingleton<TimerJobScheduler>();
                services.AddSingleton<IJobScheduler>(sp => sp.GetRequiredService<TimerJobScheduler>());
                services.AddHostedService(sp => sp.GetRequiredService<TimerJobScheduler>());
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown value '{provider}' for {ConfigKeys.JobsProvider}. Allowed values: {HangfireProvider}, {TimerProvider}.");
        }

        services.AddSingleton(new JobsProviderInfo(provider));
        services.AddScoped<IStartupTask, JobsStartupTask>();
    }

    private static void ConfigureHangfire(IServiceCollection services, IConfiguration configuration)
    {
        var contentRoot = configuration["contentRoot"] ?? AppContext.BaseDirectory;
        var hangfirePath = Path.Combine(StoragePaths.DataDirectory(contentRoot, configuration), HangfireFileName);

        // Full-mutex connections serialise access inside SQLite, because the storage
        // package's lock heartbeat and its scheduler can touch one connection from
        // two threads.
        var connectionFactory = new SQLiteDbConnectionFactory(() => new SQLiteConnection(
            hangfirePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex,
            storeDateTimeAsTicks: true)
        {
            BusyTimeout = TimeSpan.FromSeconds(10)
        });

        services.AddHangfire(config => config
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSQLiteStorage(connectionFactory, new SQLiteStorageOptions
            {
                QueuePollInterval = TimeSpan.FromSeconds(2),
                DistributedLockLifetime = TimeSpan.FromMinutes(2)
            }));

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 5;
            options.SchedulePollingInterval = TimeSpan.FromSeconds(2);
        });

        services.AddSingleton<IJobScheduler, HangfireJobScheduler>();
    }
}

public sealed record JobsProviderInfo(string Name);
