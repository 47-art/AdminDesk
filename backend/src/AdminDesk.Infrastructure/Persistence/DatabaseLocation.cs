using AdminDesk.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace AdminDesk.Infrastructure.Persistence;

// Resolves the application database file and its connection string once.
public sealed class DatabaseLocation
{
    public const string FileName = "app.db";

    public DatabaseLocation(string contentRoot, IConfiguration configuration)
    {
        var directory = StoragePaths.DataDirectory(contentRoot, configuration);
        FilePath = Path.Combine(directory, FileName);
        ConnectionString = $"Data Source={FilePath};Default Timeout=30;Foreign Keys=True";
    }

    public string FilePath { get; }

    public string ConnectionString { get; }
}
