using AdminDesk.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AdminDesk.Infrastructure.Logging;

public sealed class LoggingModule : IServiceModule
{
    public int Order => 20;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IStartupTask, LogRetentionPurgeTask>();
        services.AddHostedService<LogFlushHostedService>();
    }
}

// Lets queued warnings reach the table before the process exits.
public sealed class LogFlushHostedService : IHostedService
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var sink = DbLogSinkContributor.Current;
        return sink is null ? Task.CompletedTask : sink.FlushAsync(FlushTimeout);
    }
}
