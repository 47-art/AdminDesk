using System.Data.Common;
using AdminDesk.Application.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace AdminDesk.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseLocation _location;

    public SqliteConnectionFactory(DatabaseLocation location)
    {
        _location = location;
    }

    // A new connection per call; connections are never shared across threads.
    public async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_location.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
