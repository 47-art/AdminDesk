using System.Data.Common;

namespace AdminDesk.Application.Abstractions.Persistence;

// Runs a piece of work inside one write transaction on its own connection. The work
// is committed when it returns and rolled back when it throws. Repositories that
// write take the DbTransaction and use its Connection.
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<DbConnection, DbTransaction, Task<T>> work, CancellationToken ct);
}
