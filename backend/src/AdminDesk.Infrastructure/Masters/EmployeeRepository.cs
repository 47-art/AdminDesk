using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Masters;
using AdminDesk.Infrastructure.Persistence;
using Dapper;

namespace AdminDesk.Infrastructure.Masters;

// Dapper access to employees. Reads only; every query uses parameters and a fixed order.
public sealed class EmployeeRepository : IEmployeeRepository
{
    private static readonly string Active = AuditSql.Active("e");

    private static readonly string RecordSelect =
        "SELECT e.id AS Id, e.employee_code AS Code, e.full_name AS FullName, e.email AS Email, " +
        "e.designation AS Designation, e.department_id AS DepartmentId, d.name AS DepartmentName, " +
        "e.location_id AS LocationId, l.name AS LocationName, " +
        "e.reporting_manager_id AS ReportingManagerId, m.full_name AS ReportingManagerName " +
        "FROM employees e " +
        "LEFT JOIN departments d ON d.id = e.department_id " +
        "LEFT JOIN locations l ON l.id = e.location_id " +
        "LEFT JOIN employees m ON m.id = e.reporting_manager_id ";

    private const string TeamFrom =
        "FROM employees e " +
        "LEFT JOIN departments d ON d.id = e.department_id " +
        "LEFT JOIN locations l ON l.id = e.location_id ";

    private const string TeamColumns =
        "SELECT e.id AS Id, e.employee_code AS Code, e.full_name AS FullName, e.email AS Email, " +
        "e.designation AS Designation, d.name AS DepartmentName, l.name AS LocationName ";

    private readonly IDbConnectionFactory _factory;
    private readonly ISqlDialect _dialect;

    public EmployeeRepository(IDbConnectionFactory factory, ISqlDialect dialect)
    {
        _factory = factory;
        _dialect = dialect;
    }

    public Task<EmployeeRecord?> GetByIdAsync(long id, CancellationToken ct) =>
        GetOneAsync("e.id = @Value", id, ct);

    public Task<EmployeeRecord?> GetByCodeAsync(string code, CancellationToken ct) =>
        GetOneAsync("e.employee_code = @Value", code, ct);

    public Task<EmployeeRecord?> GetByEmailAsync(string email, CancellationToken ct) =>
        GetOneAsync("e.email = @Value COLLATE NOCASE", email, ct);

    private async Task<EmployeeRecord?> GetOneAsync(string predicate, object value, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<EmployeeRecord>(new CommandDefinition(
            RecordSelect + "WHERE " + predicate + " AND " + Active + " LIMIT 1",
            new { Value = value }, cancellationToken: ct));
    }

    public async Task<bool> HasDirectReportsAsync(long employeeId, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM employees e WHERE e.reporting_manager_id = @Id AND " + Active,
            new { Id = employeeId }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<long?> GetReportingManagerIdAsync(long employeeId, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT m.id FROM employees e JOIN employees m ON m.id = e.reporting_manager_id " +
            "WHERE e.id = @Id AND " + Active + " AND " + AuditSql.Active("m"),
            new { Id = employeeId }, cancellationToken: ct));
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM employees e WHERE " + Active, cancellationToken: ct));
    }

    public Task<PagedResult<TeamMemberRow>> ListDirectReportsAsync(long managerId, string? q, int page, int pageSize, CancellationToken ct) =>
        ListAsync("e.reporting_manager_id = @ManagerId AND ", new { ManagerId = managerId }, q, page, pageSize, ct);

    public Task<PagedResult<TeamMemberRow>> ListAllAsync(string? q, int page, int pageSize, CancellationToken ct) =>
        ListAsync(string.Empty, new { }, q, page, pageSize, ct);

    private async Task<PagedResult<TeamMemberRow>> ListAsync(
        string scope, object scopeParameters, string? q, int page, int pageSize, CancellationToken ct)
    {
        var parameters = new DynamicParameters(scopeParameters);
        var search = string.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            parameters.Add("Q", "%" + _dialect.EscapeLikeValue(q.Trim()) + "%");
            search = " AND (" + _dialect.Like("e.full_name", "@Q") + " OR " + _dialect.Like("e.employee_code", "@Q") + ")";
        }
        parameters.Add("Limit", pageSize);
        parameters.Add("Offset", (page - 1) * pageSize);

        var where = "WHERE " + scope + Active + search + " ";
        var sql =
            "SELECT COUNT(*) FROM employees e " + where + ";" +
            TeamColumns + TeamFrom + where + "ORDER BY e.full_name, e.id " + _dialect.LimitOffset("@Limit", "@Offset");

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
        var total = await multi.ReadSingleAsync<int>();
        var items = (await multi.ReadAsync<TeamMemberRow>()).ToList();
        return new PagedResult<TeamMemberRow>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<LookupItem>> SearchLookupAsync(string q, int take, CancellationToken ct)
    {
        var sql =
            "SELECT e.id AS Id, e.employee_code AS Code, e.full_name AS Label, " +
            "e.employee_code || ' - ' || COALESCE(d.name, '') AS Secondary " +
            "FROM employees e LEFT JOIN departments d ON d.id = e.department_id " +
            "WHERE " + Active + " AND (" + _dialect.Like("e.full_name", "@Q") + " OR " + _dialect.Like("e.employee_code", "@Q") + ") " +
            "ORDER BY e.full_name, e.id " + _dialect.LimitOffset("@Limit", "@Offset");

        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<LookupRow>(new CommandDefinition(
            sql,
            new { Q = "%" + _dialect.EscapeLikeValue(q) + "%", Limit = take, Offset = 0 },
            cancellationToken: ct));
        return rows.Select(r => new LookupItem(r.Id, r.Code, r.Label, r.Secondary as string)).ToList();
    }

    // Computed columns have no declared type, so the text column is read as an object.
    private sealed class LookupRow
    {
        public long Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public object? Secondary { get; set; }
    }
}
