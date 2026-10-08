using System.Data.Common;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Infrastructure.Persistence;
using Dapper;

namespace AdminDesk.Infrastructure.Definitions;

// Dapper access to stored definition versions. Rows are only added, never changed or removed.
public sealed class DefinitionRepository : IDefinitionRepository
{
    private const string Columns =
        "d.id AS Id, d.code AS Code, d.version AS Version, d.name AS Name, d.category AS Category, " +
        "d.prefix AS Prefix, d.definition_json AS DefinitionJson, d.content_hash AS ContentHash";

    private readonly IDbConnectionFactory _factory;
    private readonly AuditStamper _stamper;

    public DefinitionRepository(IDbConnectionFactory factory, AuditStamper stamper)
    {
        _factory = factory;
        _stamper = stamper;
    }

    public async Task<long> InsertAsync(DbTransaction transaction, DefinitionRow row, CancellationToken ct)
    {
        // Definitions are written only by the startup sync, so the stamp is the system actor.
        var stamp = _stamper.ForSystem();
        var parameters = new DynamicParameters(stamp);
        parameters.Add("Code", row.Code);
        parameters.Add("Version", row.Version);
        parameters.Add("Name", row.Name);
        parameters.Add("Category", row.Category);
        parameters.Add("Prefix", row.Prefix);
        parameters.Add("Json", row.DefinitionJson);
        parameters.Add("Hash", row.ContentHash);

        var sql =
            "INSERT INTO module_definitions (code, version, name, category, prefix, definition_json, content_hash, " +
            AuditSql.InsertColumns + ") " +
            "VALUES (@Code, @Version, @Name, @Category, @Prefix, @Json, @Hash, " + AuditSql.InsertValues + ");" +
            "SELECT last_insert_rowid();";
        return await transaction.Connection!.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
    }

    public async Task<int?> MaxVersionAsync(string code, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT MAX(version) FROM module_definitions WHERE code = @Code", new { Code = code }, cancellationToken: ct));
    }

    public async Task<DefinitionRow?> GetByIdAsync(long id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<DefinitionRow>(new CommandDefinition(
            "SELECT " + Columns + " FROM module_definitions d WHERE d.id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DefinitionRow>> ListLatestAsync(CancellationToken ct)
    {
        var active = AuditSql.Active("d");
        var inner = AuditSql.Active("x");
        var sql =
            "SELECT " + Columns + " FROM module_definitions d WHERE " + active +
            " AND d.version = (SELECT MAX(x.version) FROM module_definitions x WHERE x.code = d.code AND " + inner + ") " +
            "ORDER BY d.code";
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<DefinitionRow>(new CommandDefinition(sql, cancellationToken: ct))).ToList();
    }

    public async Task<string?> GetHashAsync(string code, int version, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT content_hash FROM module_definitions WHERE code = @Code AND version = @Version",
            new { Code = code, Version = version }, cancellationToken: ct));
    }
}
