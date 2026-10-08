using System.Data.Common;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
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
        return (await connection.QueryAsync<StoredLimit>(new CommandDefinition(sql, new { ModuleCode = moduleCode }, cancellationToken: ct)))
            .Select(row => row.ToRow())
            .ToList();
    }

    public async Task<IReadOnlyList<LimitRow>> ListAllAsync(CancellationToken ct)
    {
        var sql =
            "SELECT " + Columns + " FROM module_limits l WHERE " + AuditSql.Active("l") +
            " ORDER BY l.module_code, l.step_key, l.limit_key";
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<StoredLimit>(new CommandDefinition(sql, cancellationToken: ct)))
            .Select(row => row.ToRow())
            .ToList();
    }

    public async Task<LimitRow?> GetAsync(string moduleCode, string stepKey, string limitKey, CancellationToken ct)
    {
        var sql =
            "SELECT " + Columns + " FROM module_limits l WHERE l.module_code = @ModuleCode AND l.step_key = @StepKey " +
            "AND l.limit_key = @LimitKey AND " + AuditSql.Active("l");
        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<StoredLimit>(
            new CommandDefinition(sql, new { ModuleCode = moduleCode, StepKey = stepKey, LimitKey = limitKey }, cancellationToken: ct));
        return row?.ToRow();
    }

    public async Task<long?> UpdateValueAsync(
        DbTransaction transaction, string moduleCode, string stepKey, string limitKey, long newValueMinor, CancellationToken ct)
    {
        var where = "module_code = @ModuleCode AND step_key = @StepKey AND limit_key = @LimitKey AND " +
                    AuditColumns.IsActive + " = 1 AND " + AuditColumns.DeletedUtc + " IS NULL";
        var parameters = new DynamicParameters(_stamper.ForUpdate());
        parameters.Add("ModuleCode", moduleCode);
        parameters.Add("StepKey", stepKey);
        parameters.Add("LimitKey", limitKey);
        parameters.Add("ValueMinor", newValueMinor);

        var previous = await transaction.Connection!.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT value_minor FROM module_limits WHERE " + where, parameters, transaction, cancellationToken: ct));
        if (previous is null)
        {
            return null;
        }
        // A value set by a person is no longer the shipped sample value.
        await transaction.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE module_limits SET value_minor = @ValueMinor, is_sample = 0, " + AuditSql.UpdateSet + " WHERE " + where,
            parameters, transaction, cancellationToken: ct));
        return previous;
    }

    // The database hands back 64-bit integers, so rows are read into settable properties first.
    private sealed class StoredLimit
    {
        public string ModuleCode { get; set; } = string.Empty;
        public string StepKey { get; set; } = string.Empty;
        public string LimitKey { get; set; } = string.Empty;
        public long ValueMinor { get; set; }
        public string? Unit { get; set; }
        public long IsSample { get; set; }

        public LimitRow ToRow() => new(ModuleCode, StepKey, LimitKey, ValueMinor, Unit, IsSample != 0);
    }
}
