using System.Data.Common;

namespace AdminDesk.Application.Abstractions.Persistence;

// Opens a new, unshared database connection per call. The caller disposes it.
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken ct);
}
