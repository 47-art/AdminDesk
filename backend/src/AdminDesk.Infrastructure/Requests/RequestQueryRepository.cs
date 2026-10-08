using System.Globalization;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Requests;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using Dapper;

namespace AdminDesk.Infrastructure.Requests;

// Read-only Dapper queries behind the request lists, the detail and the audit trail.
// Every value reaches the SQL as a parameter; the only text taken from the caller that
// influences the statement (sort and direction) is looked up in fixed lists below.
public sealed class RequestQueryRepository : IRequestQueryRepository
{
    private const string DateFormat = "yyyy-MM-dd";

    // Status and approval values as SQL literals, taken from the enums so the names live in one place.
    private static string Lit<T>(T value) where T : Enum => "'" + value + "'";

    private static readonly string InProgress = Lit(RequestStatus.InProgress);

    private static readonly string StatusOrder =
        "CASE r.current_status WHEN " + InProgress + " THEN 0 WHEN " + Lit(RequestStatus.Closed) + " THEN 1 WHEN " +
        Lit(RequestStatus.Rejected) + " THEN 2 WHEN " + Lit(RequestStatus.Cancelled) + " THEN 3 ELSE 4 END";

    // Sort keys the client may use, mapped to the column or expression behind them.
    private static readonly IReadOnlyDictionary<string, string> SortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requestNo"] = "r.request_no",
            ["requestDate"] = "r.request_date",
            ["status"] = StatusOrder,
            ["updatedUtc"] = "r.updated_utc"
        };

    private const string DefaultSortKey = "requestDate";

    private const string ListColumns =
        "r.id AS Id, r.request_no AS RequestNo, r.module_code AS ModuleCode, r.definition_id AS DefinitionId, " +
        "r.subject AS Subject, r.request_date AS RequestDate, r.required_date AS RequiredDate, " +
        "r.priority AS Priority, r.current_status AS CurrentStatus, r.current_step_key AS CurrentStepKey, " +
        "cs.name AS CurrentStepName, resp.full_name AS ResponsibleName, r.responsible_role AS ResponsibleRole, " +
        "r.updated_utc AS UpdatedUtc, r.created_utc AS CreatedUtc, r.closed_utc AS ClosedUtc, " +
        "re.full_name AS RequesterName, rd.name AS RequesterDepartment";

    private const string ListFrom =
        " FROM requests r " +
        "JOIN employees re ON re.id = r.requester_employee_id " +
        "LEFT JOIN departments rd ON rd.id = r.department_id " +
        "LEFT JOIN request_steps cs ON cs.request_id = r.id AND cs.seq = r.current_step_seq " +
        "LEFT JOIN employees resp ON resp.id = r.responsible_employee_id ";

    // The count needs only the tables the filters touch.
    private const string CountFrom =
        " FROM requests r JOIN employees re ON re.id = r.requester_employee_id ";

    private const string HeaderSql =
        "SELECT r.id AS Id, r.request_no AS RequestNo, r.module_code AS ModuleCode, r.definition_id AS DefinitionId, " +
        "r.requester_employee_id AS RequesterEmployeeId, re.employee_code AS RequesterCode, re.full_name AS RequesterName, " +
        "d.name AS DepartmentName, r.project_id AS ProjectId, p.name AS ProjectName, r.location_id AS LocationId, " +
        "l.name AS LocationName, r.cost_centre_id AS CostCentreId, cc.name AS CostCentreName, " +
        "r.request_date AS RequestDate, r.required_date AS RequiredDate, r.priority AS Priority, r.subject AS Subject, " +
        "r.approval_status AS ApprovalStatus, r.current_status AS CurrentStatus, r.current_step_key AS CurrentStepKey, " +
        "r.current_step_seq AS CurrentStepSeq, resp.full_name AS ResponsibleName, r.responsible_role AS ResponsibleRole, " +
        "r.remarks AS Remarks, r.payload_json AS PayloadJson, r.row_version AS RowVersion, " +
        "r.created_utc AS CreatedUtc, r.closed_utc AS ClosedUtc " +
        "FROM requests r " +
        "JOIN employees re ON re.id = r.requester_employee_id " +
        "LEFT JOIN departments d ON d.id = r.department_id " +
        "LEFT JOIN projects p ON p.id = r.project_id " +
        "LEFT JOIN locations l ON l.id = r.location_id " +
        "LEFT JOIN cost_centres cc ON cc.id = r.cost_centre_id " +
        "LEFT JOIN employees resp ON resp.id = r.responsible_employee_id ";

    private const string StepsSql =
        "SELECT s.id, s.request_id, s.seq, s.step_key, s.name, s.step_type, s.state, s.activated_utc, s.due_utc, " +
        "s.acted_by_user_id, s.acted_by_name, s.acted_utc, s.comment, s.captured_json " +
        "FROM request_steps s WHERE s.request_id = @Id ORDER BY s.seq";

    private const string ActorsSql =
        "SELECT a.id, a.request_id, a.step_seq, a.role_name, a.employee_id FROM request_step_actors a " +
        "WHERE a.request_id = @Id AND a.is_active = 1 ORDER BY a.step_seq, a.id";

    private static readonly string StopSql =
        "SELECT e.event_type AS EventType, e.comment AS Comment, e.actor_name AS ActorName, e.actor_role AS ActorRole, " +
        "e.created_utc AS CreatedUtc FROM audit_events e " +
        "WHERE e.request_id = @Id AND e.event_type IN ('" + AuditEventTypes.Cancelled + "', '" + AuditEventTypes.Rejected +
        "') ORDER BY e.id DESC LIMIT 1";

    private readonly IDbConnectionFactory _factory;
    private readonly ISqlDialect _dialect;

    public RequestQueryRepository(IDbConnectionFactory factory, ISqlDialect dialect)
    {
        _factory = factory;
        _dialect = dialect;
    }

    public async Task<bool> ExistsAsync(long id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var found = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM requests r WHERE r.id = @Id AND " + AuditSql.Active("r") + ")",
            new { Id = id }, cancellationToken: ct));
        return found == 1;
    }

    public async Task<RequestDetailRows?> GetDetailAsync(long id, CancellationToken ct)
    {
        var sql = HeaderSql + "WHERE r.id = @Id AND " + AuditSql.Active("r") + "; " + StepsSql + "; " + ActorsSql + "; " + StopSql;

        await using var connection = await _factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));

        var header = await grid.ReadSingleOrDefaultAsync<RequestHeaderRow>();
        var steps = (await grid.ReadAsync<RequestStepRow>()).ToList();
        var actors = (await grid.ReadAsync<ActorRow>()).ToList();
        var stopped = (await grid.ReadAsync<StopEventRow>()).FirstOrDefault();

        if (header is null)
        {
            return null;
        }
        return new RequestDetailRows { Header = header, Steps = steps, ActiveActors = actors, Stopped = stopped };
    }

    // ------------------------------------------------------------------ mine

    public Task<PagedRows<RequestListRow>> ListMineAsync(long employeeId, MineFilter filter, CancellationToken ct) =>
        ListFilteredAsync("r.requester_employee_id = @Who", employeeId, filter, ct);

    public Task<PagedRows<RequestListRow>> ListAllAsync(MineFilter filter, CancellationToken ct) =>
        ListFilteredAsync(null, null, filter, ct);

    public Task<PagedRows<RequestListRow>> ListTeamAsync(long managerEmployeeId, MineFilter filter, CancellationToken ct) =>
        ListFilteredAsync("re.reporting_manager_id = @Who", managerEmployeeId, filter, ct);

    // One filter builder for the three request lists; only the "who" clause differs.
    private async Task<PagedRows<RequestListRow>> ListFilteredAsync(
        string? whoClause, long? who, MineFilter filter, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var where = new List<string> { AuditSql.Active("r") };
        if (whoClause is not null)
        {
            where.Add(whoClause);
            parameters.Add("Who", who);
        }

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            where.Add("(" + _dialect.Like("r.request_no", "@Q") + " OR " + _dialect.Like("r.subject", "@Q") + ")");
            parameters.Add("Q", "%" + _dialect.EscapeLikeValue(filter.Q.Trim()) + "%");
        }
        if (filter.Statuses.Count > 0)
        {
            where.Add("r.current_status IN @Statuses");
            parameters.Add("Statuses", filter.Statuses.Select(s => s.ToString()).ToArray());
        }
        if (filter.ApprovalStatus is { } approval)
        {
            where.Add("r.approval_status = @Approval");
            parameters.Add("Approval", approval.ToString());
        }
        if (!string.IsNullOrWhiteSpace(filter.Module))
        {
            where.Add("r.module_code = @Module");
            parameters.Add("Module", filter.Module);
        }
        if (filter.From is { } from)
        {
            where.Add("r.request_date >= @From");
            parameters.Add("From", from.ToString(DateFormat, CultureInfo.InvariantCulture));
        }
        if (filter.To is { } to)
        {
            where.Add("r.request_date <= @To");
            parameters.Add("To", to.ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        var orderBy = OrderBy(filter.Sort, filter.Dir);
        return await PageAsync(where, parameters, orderBy, filter.Page, filter.PageSize, ct);
    }

    // Looks the caller's sort text up in the fixed list; anything unknown falls back to the request date.
    private static string OrderBy(string? sort, string? dir)
    {
        var column = sort is not null && SortColumns.TryGetValue(sort.Trim(), out var known)
            ? known
            : SortColumns[DefaultSortKey];
        var direction = string.Equals(dir?.Trim(), SortDirections.Ascending, StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        return column + " " + direction + ", r.id " + direction;
    }

    // ----------------------------------------------------------------- inbox

    public async Task<PagedRows<RequestListRow>> ListInboxAsync(
        long? employeeId, IReadOnlyCollection<string> roles, InboxFilter filter, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var where = new List<string> { AuditSql.Active("r"), "r.current_status = " + InProgress };
        if (!AddActorClause(where, parameters, employeeId, roles))
        {
            return new PagedRows<RequestListRow>(Array.Empty<RequestListRow>(), 0);
        }

        if (!string.IsNullOrWhiteSpace(filter.Module))
        {
            where.Add("r.module_code = @Module");
            parameters.Add("Module", filter.Module);
        }
        if (!string.IsNullOrWhiteSpace(filter.Requester))
        {
            where.Add(_dialect.Like("re.full_name", "@Requester"));
            parameters.Add("Requester", "%" + _dialect.EscapeLikeValue(filter.Requester.Trim()) + "%");
        }
        if (filter.Priority is { } priority)
        {
            where.Add("r.priority = @Priority");
            parameters.Add("Priority", priority.ToString());
        }

        // Oldest first.
        return await PageAsync(where, parameters, "r.created_utc ASC, r.id ASC", filter.Page, filter.PageSize, ct);
    }

    public async Task<int> CountInboxAsync(long? employeeId, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var where = new List<string> { AuditSql.Active("r"), "r.current_status = " + InProgress };
        if (!AddActorClause(where, parameters, employeeId, roles))
        {
            return 0;
        }

        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*)" + CountFrom + "WHERE " + string.Join(" AND ", where), parameters, cancellationToken: ct));
    }

    // The request's current step has the caller as an active actor, by employee id or by role.
    // Returns false when the caller can match nothing at all.
    private static bool AddActorClause(
        List<string> where, DynamicParameters parameters, long? employeeId, IReadOnlyCollection<string> roles)
    {
        var matches = new List<string>();
        if (employeeId is { } employee)
        {
            matches.Add("a.employee_id = @Employee");
            parameters.Add("Employee", employee);
        }
        if (roles.Count > 0)
        {
            matches.Add("a.role_name IN @Roles");
            parameters.Add("Roles", roles.ToArray());
        }
        if (matches.Count == 0)
        {
            return false;
        }

        where.Add(
            "EXISTS (SELECT 1 FROM request_step_actors a WHERE a.request_id = r.id AND a.step_seq = r.current_step_seq " +
            "AND a.is_active = 1 AND (" + string.Join(" OR ", matches) + "))");
        return true;
    }

    // ---------------------------------------------------------------- summary

    public async Task<SummaryCounts> SummaryAsync(long? employeeId, CancellationToken ct)
    {
        var sql =
            "SELECT COUNT(*) AS Total, " +
            "COALESCE(SUM(CASE WHEN r.current_status = " + InProgress + " AND r.approval_status = " + Lit(ApprovalStatus.Pending) + " THEN 1 ELSE 0 END), 0) AS Pending, " +
            "COALESCE(SUM(CASE WHEN r.approval_status = " + Lit(ApprovalStatus.Approved) + " THEN 1 ELSE 0 END), 0) AS Approved, " +
            "COALESCE(SUM(CASE WHEN r.current_status = " + Lit(RequestStatus.Rejected) + " THEN 1 ELSE 0 END), 0) AS Rejected, " +
            "COALESCE(SUM(CASE WHEN r.current_status = " + Lit(RequestStatus.Closed) + " THEN 1 ELSE 0 END), 0) AS Completed, " +
            "COALESCE(SUM(CASE WHEN r.current_status = " + Lit(RequestStatus.Cancelled) + " THEN 1 ELSE 0 END), 0) AS Cancelled " +
            "FROM requests r WHERE (@Employee IS NULL OR r.requester_employee_id = @Employee) AND ";

        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<SummaryCounts>(new CommandDefinition(
            sql + AuditSql.Active("r"), new { Employee = employeeId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RequestListRow>> ListRecentAsync(long employeeId, string userId, int take, CancellationToken ct)
    {
        var sql =
            "SELECT " + ListColumns + ListFrom +
            "WHERE " + AuditSql.Active("r") + " AND (r.requester_employee_id = @Employee OR EXISTS (" +
            "SELECT 1 FROM request_steps s WHERE s.request_id = r.id AND s.acted_by_user_id = @UserId)) " +
            "ORDER BY r.updated_utc DESC, r.id DESC " + _dialect.LimitOffset("@Limit", "@Offset");

        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<RequestListRow>(new CommandDefinition(
            sql, new { Employee = employeeId, UserId = userId, Limit = take, Offset = 0 }, cancellationToken: ct));
        return rows.ToList();
    }

    // ------------------------------------------------------------------ audit

    public async Task<IReadOnlyList<AuditRow>> ListAuditAsync(long requestId, CancellationToken ct)
    {
        // The step's display name comes from the request's own step rows; events without a step get null.
        var sql =
            "SELECT e.id AS Id, e.event_type AS EventType, e.actor_name AS ActorName, e.actor_role AS ActorRole, " +
            "e.step_key AS StepKey, (SELECT s.name FROM request_steps s WHERE s.request_id = e.request_id " +
            "AND s.step_key = e.step_key ORDER BY s.seq " + _dialect.LimitOffset("1", "0") + ") AS StepName, e.from_status AS FromStatus, e.to_status AS ToStatus, e.comment AS Comment, " +
            "e.created_utc AS CreatedUtc FROM audit_events e WHERE e.request_id = @Id ORDER BY e.id";

        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<AuditRow>(new CommandDefinition(sql, new { Id = requestId }, cancellationToken: ct));
        return rows.ToList();
    }

    // ---------------------------------------------------------------- paging

    // One page of rows and the total in a single round trip.
    private async Task<PagedRows<RequestListRow>> PageAsync(
        List<string> where, DynamicParameters parameters, string orderBy, int page, int pageSize, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize, 1, RequestParsing.MaxPageSize);
        var number = Math.Max(1, page);
        parameters.Add("Limit", size);
        parameters.Add("Offset", (number - 1) * size);

        var condition = " WHERE " + string.Join(" AND ", where);
        var sql =
            "SELECT " + ListColumns + ListFrom + condition + " ORDER BY " + orderBy + " " +
            _dialect.LimitOffset("@Limit", "@Offset") + "; " +
            "SELECT COUNT(*)" + CountFrom + condition;

        await using var connection = await _factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
        var items = (await grid.ReadAsync<RequestListRow>()).ToList();
        var total = await grid.ReadSingleAsync<long>();
        return new PagedRows<RequestListRow>(items, (int)total);
    }
}
