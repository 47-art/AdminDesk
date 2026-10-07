using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using DbUp;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Persistence;

// Switches the database to WAL, then applies the embedded numbered scripts that have
// not run yet, one transaction per script.
public sealed class DatabaseMigrationTask : IStartupTask
{
    private readonly IDbConnectionFactory _factory;
    private readonly DatabaseLocation _location;
    private readonly ILogger<DatabaseMigrationTask> _logger;

    public DatabaseMigrationTask(IDbConnectionFactory factory, DatabaseLocation location, ILogger<DatabaseMigrationTask> logger)
    {
        _factory = factory;
        _location = location;
        _logger = logger;
    }

    public int Order => 10;

    public async Task RunAsync(CancellationToken ct)
    {
        await using (var connection = await _factory.OpenAsync(ct))
        {
            // The journal mode cannot be changed inside a transaction.
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync(ct);
        }

        var upgrader = DeployChanges.To
            .SqliteDatabase(_location.ConnectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrationTask).Assembly, name => name.Contains(".Migrations.Scripts.", StringComparison.Ordinal))
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database upgrade failed", result.Error);
        }

        _logger.LogInformation("Database is up to date ({Count} scripts applied this start)", result.Scripts.Count());
    }
}
