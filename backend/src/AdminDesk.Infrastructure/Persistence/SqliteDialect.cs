using System.Text.RegularExpressions;
using AdminDesk.Application.Abstractions.Persistence;

namespace AdminDesk.Infrastructure.Persistence;

public sealed partial class SqliteDialect : ISqlDialect
{
    public const char LikeEscape = '\\';

    public string UpsertCounterReturningSql =>
        "INSERT INTO request_counters(module_code, year, last_value) VALUES(@ModuleCode, @Year, 1) " +
        "ON CONFLICT(module_code, year) DO UPDATE SET last_value = last_value + 1 " +
        "RETURNING last_value";

    public string JsonExtract(string column, string path)
    {
        if (!SafeColumn().IsMatch(column) || !SafeJsonPath().IsMatch(path))
        {
            throw new ArgumentException("Unsafe column or JSON path.");
        }
        return $"json_extract({column}, '{path}')";
    }

    public string LimitOffset(string limitParam, string offsetParam) => $"LIMIT {limitParam} OFFSET {offsetParam}";

    public string EqualsIgnoreCase(string column, string param) => $"{column} = {param} COLLATE NOCASE";

    // SQLITE_CONSTRAINT with a unique or primary key extended code.
    public bool IsUniqueViolation(Exception exception) =>
        exception is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 or 1555 };

    public string InsertReturningId(string insertSql) => insertSql + " RETURNING id";

    public string NowUtc => "strftime('%Y-%m-%dT%H:%M:%fZ', 'now')";

    public string Like(string column, string param) => $"{column} LIKE {param} ESCAPE '{LikeEscape}'";

    public string EscapeLikeValue(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial Regex SafeColumn();

    [GeneratedRegex(@"^\$[A-Za-z0-9_.\[\]]*$")]
    private static partial Regex SafeJsonPath();
}
