using System.Data;
using System.Data.Common;
using AdminDesk.Application.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace AdminDesk.Infrastructure.Persistence;

public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly IDbConnectionFactory _factory;

    public SqliteUnitOfWork(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<DbConnection, DbTransaction, Task<T>> work, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        // An immediate transaction takes the write lock up front; a deferred one that
        // upgrades later can fail with a busy error.
        var sqlite = (SqliteConnection)connection;
        await using var transaction = sqlite.BeginTransaction(IsolationLevel.Serializable, deferred: false);
        try
        {
            var result = await work(connection, transaction);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
