using AdminDesk.SharedKernel.Constants;
using Microsoft.Extensions.Configuration;

namespace AdminDesk.Infrastructure.Configuration;

public static class StoragePaths
{
    public const string DataDirectoryVariable = "ADMINDESK_DATA_DIR";
    public const string LogDirectoryVariable = "ADMINDESK_LOG_DIR";

    // Walks up from the content root to the folder that holds backend/AdminDesk.sln.
    public static string ResolveRoot(string contentRoot)
    {
        var dir = new DirectoryInfo(contentRoot);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "backend", "AdminDesk.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return contentRoot;
    }

    public static string DataDirectory(string contentRoot, IConfiguration configuration)
    {
        var root = ResolveRoot(contentRoot);
        var fromEnv = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        string path;
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            path = fromEnv;
        }
        else
        {
            var configured = configuration[ConfigKeys.StorageDataDirectory];
            path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(root, "data")
                : Path.GetFullPath(configured, root);
        }
        Directory.CreateDirectory(path);
        return path;
    }

    public static string LogsDirectory(string contentRoot)
    {
        var fromEnv = Environment.GetEnvironmentVariable(LogDirectoryVariable);
        var path = !string.IsNullOrWhiteSpace(fromEnv)
            ? fromEnv
            : Path.Combine(ResolveRoot(contentRoot), "logs");
        Directory.CreateDirectory(path);
        return path;
    }
}
