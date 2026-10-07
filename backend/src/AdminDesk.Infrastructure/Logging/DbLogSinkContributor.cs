using AdminDesk.Infrastructure.Configuration;
using AdminDesk.SharedKernel.Constants;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace AdminDesk.Infrastructure.Logging;

// Attaches the database sink for Warning and above. Information-level request logs
// are never persisted.
public sealed class DbLogSinkContributor : ILogSinkContributor
{
    // The sink created for this process, kept so a shutdown hook can flush it.
    public static DbLogSink? Current { get; private set; }

    public int Order => 100;

    public void Configure(LoggerConfiguration logger, IConfiguration configuration, Action<string, Exception?> reportFailure)
    {
        try
        {
            var enabledText = configuration[ConfigKeys.LoggingDbEnabled];
            if (bool.TryParse(enabledText, out var enabled) && !enabled)
            {
                return;
            }

            var path = configuration[ConfigKeys.LoggingDbDatabasePath];
            if (string.IsNullOrWhiteSpace(path))
            {
                var contentRoot = configuration["contentRoot"] ?? AppContext.BaseDirectory;
                path = Path.Combine(StoragePaths.DataDirectory(contentRoot, configuration), "app.db");
            }

            // ReadWrite makes a wrong path fail instead of creating a stray file; no pooling so
            // the connection is really closed after each batch; the short timeout keeps this
            // background writer from holding the writer lock for long.
            var connectionString = $"Data Source={path};Mode=ReadWrite;Default Timeout=5;Pooling=False";

            var sink = new DbLogSink(connectionString, reportFailure);
            Current = sink;
            logger.WriteTo.Sink(sink, restrictedToMinimumLevel: LogEventLevel.Warning);
        }
        catch (Exception ex)
        {
            reportFailure("Database log sink could not be configured", ex);
        }
    }
}
