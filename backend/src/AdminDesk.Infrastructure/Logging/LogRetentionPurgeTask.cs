using System.Globalization;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Logging;

// Removes log rows older than the configured retention, per category, at startup.
// Log rows are operational records, not business records, so this class is the one
// place where rows are removed with a hard delete, and it only touches the logs table.
public sealed class LogRetentionPurgeTask : IStartupTask
{
    private static readonly string PurgeSql =
        $"DELETE FROM {LogConstants.Table} WHERE {LogConstants.Category} = @Category AND {LogConstants.TimestampUtc} < @Cutoff";

    private readonly IDbConnectionFactory _connections;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<LogRetentionPurgeTask> _logger;

    public LogRetentionPurgeTask(
        IDbConnectionFactory connections,
        IConfiguration configuration,
        TimeProvider time,
        ILogger<LogRetentionPurgeTask> logger)
    {
        _connections = connections;
        _configuration = configuration;
        _time = time;
        _logger = logger;
    }

    // After the database upgrade and before the identity setup.
    public int Order => 15;

    public async Task RunAsync(CancellationToken ct)
    {
        await PurgeAsync(LogConstants.CategoryApp, ConfigKeys.LoggingDbRetentionDaysApp, LogConstants.DefaultRetentionDaysApp, ct);
        await PurgeAsync(LogConstants.CategoryEmail, ConfigKeys.LoggingDbRetentionDaysEmail, LogConstants.DefaultRetentionDaysEmail, ct);
    }

    private async Task PurgeAsync(string category, string configKey, int defaultDays, CancellationToken ct)
    {
        try
        {
            var days = defaultDays;
            var configured = _configuration[configKey];
            if (!string.IsNullOrWhiteSpace(configured) && int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                days = parsed;
            }
            if (days <= 0)
            {
                return;
            }

            var cutoff = _time.GetUtcNow().UtcDateTime.AddDays(-days)
                .ToString(DapperTypeHandlers.DateTimeFormat, CultureInfo.InvariantCulture);

            await using var connection = await _connections.OpenAsync(ct);
            var removed = await connection.ExecuteAsync(
                new CommandDefinition(PurgeSql, new { Category = category, Cutoff = cutoff }, cancellationToken: ct));
            _logger.LogInformation("Log retention purge removed {Count} {Category} rows", removed, category);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Log retention purge for {Category} failed", category);
        }
    }
}
