using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Masters;
using AdminDesk.Infrastructure.Persistence;
using Dapper;

namespace AdminDesk.Infrastructure.Masters;

// Lookup over one of the simple code/name master tables. The table name is a
// constant supplied by the subclass, never a value from a request.
public abstract class SimpleMasterLookupProvider : ILookupProvider
{
    private readonly IDbConnectionFactory _factory;
    private readonly ISqlDialect _dialect;
    private readonly string _table;

    protected SimpleMasterLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect, string kind, string table)
    {
        _factory = factory;
        _dialect = dialect;
        Kind = kind;
        _table = table;
    }

    public string Kind { get; }

    private string Select => "SELECT t.id AS Id, t.code AS Code, t.name AS Label, t.code AS Secondary FROM " + _table + " t ";

    public async Task<IReadOnlyList<LookupItem>> SearchAsync(string q, int take, CancellationToken ct)
    {
        var filtered = !string.IsNullOrEmpty(q);
        var sql = Select +
            "WHERE " + AuditSql.Active("t") +
            (filtered ? " AND (" + _dialect.Like("t.name", "@Q") + " OR " + _dialect.Like("t.code", "@Q") + ") " : " ") +
            "ORDER BY t.name, t.id " + _dialect.LimitOffset("@Limit", "@Offset");

        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<LookupItem>(new CommandDefinition(
            sql,
            new { Q = filtered ? "%" + _dialect.EscapeLikeValue(q) + "%" : string.Empty, Limit = take, Offset = 0 },
            cancellationToken: ct));
        return rows.ToList();
    }

    // Label lookups do not filter by active, so an item deactivated since still shows its label on a request.
    public async Task<LookupItem?> GetAsync(long id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<LookupItem>(new CommandDefinition(
            Select + "WHERE t.id = @Id",
            new { Id = id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LookupItem>> GetManyAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<LookupItem>();
        }
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<LookupItem>(new CommandDefinition(
            Select + "WHERE t.id IN @Ids",
            new { Ids = ids.Distinct().ToArray() }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(long id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM " + _table + " t WHERE t.id = @Id AND " + AuditSql.Active("t"),
            new { Id = id }, cancellationToken: ct));
        return count > 0;
    }
}

public sealed class DepartmentLookupProvider : SimpleMasterLookupProvider
{
    public DepartmentLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect)
        : base(factory, dialect, "department", "departments")
    {
    }
}

public sealed class LocationLookupProvider : SimpleMasterLookupProvider
{
    public LocationLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect)
        : base(factory, dialect, "location", "locations")
    {
    }
}

public sealed class ProjectLookupProvider : SimpleMasterLookupProvider
{
    public ProjectLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect)
        : base(factory, dialect, "project", "projects")
    {
    }
}

public sealed class CostCentreLookupProvider : SimpleMasterLookupProvider
{
    public CostCentreLookupProvider(IDbConnectionFactory factory, ISqlDialect dialect)
        : base(factory, dialect, "costCentre", "cost_centres")
    {
    }
}

public sealed class EmployeeLookupProvider : ILookupProvider
{
    private readonly IEmployeeRepository _employees;

    public EmployeeLookupProvider(IEmployeeRepository employees)
    {
        _employees = employees;
    }

    public string Kind => "employee";

    public Task<IReadOnlyList<LookupItem>> SearchAsync(string q, int take, CancellationToken ct) =>
        _employees.SearchLookupAsync(q, take, ct);

    // Label lookups do not filter by active, so an employee deactivated since still shows on a request.
    public async Task<LookupItem?> GetAsync(long id, CancellationToken ct) =>
        (await _employees.GetLabelsAsync(new[] { id }, ct)).FirstOrDefault();

    public Task<IReadOnlyList<LookupItem>> GetManyAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        _employees.GetLabelsAsync(ids, ct);

    public async Task<bool> ExistsAsync(long id, CancellationToken ct) =>
        await _employees.GetByIdAsync(id, ct) is not null;
}
