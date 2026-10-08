using System.Data.Common;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Infrastructure.Persistence;
using Dapper;

namespace AdminDesk.Infrastructure.Definitions;

// Dapper access to module limits. A limit is added once; its value is then owned by the database.
public sealed class LimitRepository : ILimitRepository
{
    private const string Columns =
        "l.module_code AS ModuleCode, l.step_key AS StepKey, l.limit_key AS LimitKey, " +
        "l.value_minor AS ValueMinor, l.unit AS Unit, l.is_sample AS IsSample";

    private readonly IDbConnectionFactory _factory;
    private readonly AuditStamper _stamper;

    public LimitRepository(IDbConnectionFactory factory, AuditStamper stamper)
    {
        _factory = factory;
        _stamper = stamper;
    }

    public async Task InsertIfMissingAsync(DbTransaction transaction, LimitRow limit, CancellationToken ct)
    {
        // Written only by the startup sync, so the stamp is the system actor. A conflict, including with a
        // removed row, leaves the stored row exactly as it is.
        var parameters = new DynamicParameters(_stamper.ForSystem());
        parameters.Add("ModuleCode", limit.ModuleCode);
        parameters.Add("StepKey", limit.StepKey);
        parameters.Add("LimitKey", limit.LimitKey);
        parameters.Add("ValueMinor", limit.ValueMinor);
        parameters.Add("Unit", limit.Unit);
        parameters.Add("IsSample", limit.IsSample ? 1 : 0);

        var sql =
            "INSERT INTO module_limits (module_code, step_key, limit_key, value_minor, unit, is_sample, " +
            AuditSql.InsertColumns + ") " +
            "VALUES (@ModuleCode, @StepKey, @LimitKey, @ValueMinor, @Unit, @IsSample, " + AuditSql.InsertValues + ") " +
            "ON CONFLICT (module_code, step_key, limit_key) DO NOTHING";
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LimitRow>> ListForModuleAsync(string moduleCode, CancellationToken ct)
    {
        var sql =
            "SELECT " + Columns + " FROM module_limits l WHERE l.module_code = @ModuleCode AND " + AuditSql.Active("l") +
            " ORDER BY l.step_key, l.limit_key";
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<LimitRow>(new CommandDefinition(sql, new { ModuleCode = moduleCode }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<LimitRow>> ListAllAsync(CancellationToken ct)
    {
        var sql =
            "SELECT " + Columns + " FROM module_limits l WHERE " + AuditSql.Active("l") +
            " ORDER BY l.module_code, l.step_key, l.limit_key";
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<LimitRow>(new CommandDefinition(sql, cancellationToken: ct))).ToList();
    }
}
