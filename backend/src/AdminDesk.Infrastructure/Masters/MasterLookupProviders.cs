using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Masters;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
using Dapper;

namespace AdminDesk.Infrastructure.Masters;

// Lookup over the SIM or asset master. The "available" kinds offer items that can be allocated, the
// "held" kinds offer items somebody holds now. Table and column fragments are constants supplied by the
// subclass, never request values. ExistsAsync applies the same filter, so an item taken in the meantime
// no longer passes validation.
public abstract class MasterItemLookupProvider : ILookupProvider
{
    private readonly IDbConnectionFactory _factory;
    private readonly ISqlDialect _dialect;
    private readonly string _table;
    private readonly string _codeColumn;
    private readonly string _labelExpression;
    private readonly string _availableSecondary;
    private readonly string[] _searchColumns;
    private readonly bool _held;
    private readonly string _allocatedStatus;
    private readonly string _availableStatus;

    protected MasterItemLookupProvider(
        IDbConnectionFactory factory,
        ISqlDialect dialect,
        string kind,
        bool held,
        string table,
        string codeColumn,
        string labelExpression,
        string availableSecondary,
        string[] searchColumns,
        string availableStatus,
        string allocatedStatus)
    {
        _factory = factory;
        _dialect = dialect;
        Kind = kind;
        _held = held;
        _table = table;
        _codeColumn = codeColumn;
        _labelExpression = labelExpression;
        _availableSecondary = availableSecondary;
        _searchColumns = searchColumns;
        _availableStatus = availableStatus;
        _allocatedStatus = allocatedStatus;
    }

    public string Kind { get; }

    private string Select =>
        $"SELECT t.id AS Id, t.{_codeColumn} AS Code, COALESCE({_labelExpression}, t.{_codeColumn}, '') AS Label, " +
        "COALESCE(" + (_held ? "e.full_name" : _availableSecondary) + ", '') AS Secondary " +
        $"FROM {_table} t LEFT JOIN employees e ON e.id = t.holder_employee_id ";

    private string Eligible =>
        AuditSql.Active("t") + (_held
            ? $" AND t.status = '{_allocatedStatus}' AND t.holder_employee_id IS NOT NULL"
            : $" AND t.status = '{_availableStatus}'");

    public async Task<IReadOnlyList<LookupItem>> SearchAsync(string q, int take, CancellationToken ct)
    {
        var filtered = !string.IsNullOrEmpty(q);
        var search = filtered
            ? " AND (" + string.Join(" OR ", _searchColumns.Select(c => _dialect.Like("t." + c, "@Q"))) +
              (_held ? " OR " + _dialect.Like("e.full_name", "@Q") : string.Empty) + ")"
            : string.Empty;
        var sql = Select + "WHERE " + Eligible + search + $" ORDER BY t.{_codeColumn}, t.id " + _dialect.LimitOffset("@Limit", "@Offset");

        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<LookupItem>(new CommandDefinition(
            sql,
            new { Q = filtered ? "%" + _dialect.EscapeLikeValue(q) + "%" : string.Empty, Limit = take, Offset = 0 },
            cancellationToken: ct));
        return rows.ToList();
    }

    // Label lookups: an item that was allocated or returned since still has to show its label on a request,
    // so these two methods do not apply the eligibility filter. Search and ExistsAsync still do.
    public async Task<LookupItem?> GetAsync(long id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<LookupItem>(new CommandDefinition(
            Select + "WHERE t.id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LookupItem>> GetManyAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<LookupItem>();
        }
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<LookupItem>(new CommandDefinition(
            Select + "WHERE t.id IN @Ids", new { Ids = ids.Distinct().ToArray() }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(long id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM {_table} t WHERE t.id = @Id AND " + Eligible, new { Id = id }, cancellationToken: ct));
        return count > 0;
    }
}

public abstract class SimLookupProvider : MasterItemLookupProvider
{
    protected SimLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect, string kind, bool held)
        : base(factory, dialect, kind, held, "sims", "sim_number",
            "t.sim_number || ' - ' || t.mobile_number", "t.telecom_operator || ' ' || t.plan",
            new[] { "sim_number", "mobile_number", "telecom_operator", "plan" },
            SimStatuses.Available, SimStatuses.Allocated)
    {
    }
}

public abstract class AssetLookupProvider : MasterItemLookupProvider
{
    protected AssetLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect, string kind, bool held)
        : base(factory, dialect, kind, held, "assets", "asset_tag",
            "t.asset_tag || ' - ' || t.make_model", "t.asset_type",
            new[] { "asset_tag", "asset_type", "make_model", "serial_number" },
            AssetStatuses.Available, AssetStatuses.Allocated)
    {
    }
}

public sealed class AvailableSimLookupProvider : SimLookupProvider
{
    public AvailableSimLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect)
        : base(factory, dialect, MasterLookupKinds.AvailableSim, false)
    {
    }
}

public sealed class HeldSimLookupProvider : SimLookupProvider
{
    public HeldSimLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect)
        : base(factory, dialect, MasterLookupKinds.HeldSim, true)
    {
    }
}

public sealed class AvailableAssetLookupProvider : AssetLookupProvider
{
    public AvailableAssetLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect)
        : base(factory, dialect, MasterLookupKinds.AvailableAsset, false)
    {
    }
}

public sealed class HeldAssetLookupProvider : AssetLookupProvider
{
    public HeldAssetLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect)
        : base(factory, dialect, MasterLookupKinds.HeldAsset, true)
    {
    }
}
