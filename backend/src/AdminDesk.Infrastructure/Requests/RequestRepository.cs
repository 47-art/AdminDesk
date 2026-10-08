using System.Data.Common;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Engine;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
using Dapper;

namespace AdminDesk.Infrastructure.Requests;

// Dapper access to requests, their steps and their actor rows. Nothing here removes a row.
public sealed class RequestRepository : IRequestRepository
{
    private const string RequestColumns =
        "r.id, r.request_no, r.module_code, r.definition_id, r.definition_version, r.requester_employee_id, " +
        "r.department_id, r.project_id, r.location_id, r.cost_centre_id, r.request_date, r.required_date, " +
        "r.priority, r.subject, r.approval_status, r.current_status, r.current_step_key, r.current_step_seq, " +
        "r.responsible_employee_id, r.responsible_role, r.remarks, r.payload_json, r.amount_minor, " +
        "r.parent_request_id, r.row_version, r.closed_utc";

    private const string StepColumns =
        "s.id, s.request_id, s.seq, s.step_key, s.name, s.step_type, s.state, s.activated_utc, s.due_utc, " +
        "s.acted_by_user_id, s.acted_by_name, s.acted_utc, s.comment, s.captured_json";

    private readonly IDbConnectionFactory _factory;
    private readonly AuditStamper _stamper;
    private readonly ISqlDialect _dialect;

    public RequestRepository(IDbConnectionFactory factory, AuditStamper stamper, ISqlDialect dialect)
    {
        _factory = factory;
        _stamper = stamper;
        _dialect = dialect;
    }

    public async Task<long> NextCounterAsync(DbTransaction tx, string moduleCode, int year, CancellationToken ct)
    {
        return await tx.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            _dialect.UpsertCounterReturningSql, new { ModuleCode = moduleCode, Year = year }, tx, cancellationToken: ct));
    }

    public async Task<long> InsertRequestAsync(DbTransaction tx, RequestSnapshot request, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForCreate());
        p.Add("RequestNo", request.RequestNo);
        p.Add("ModuleCode", request.ModuleCode);
        p.Add("DefinitionId", request.DefinitionId);
        p.Add("DefinitionVersion", request.DefinitionVersion);
        p.Add("RequesterEmployeeId", request.RequesterEmployeeId);
        p.Add("DepartmentId", request.DepartmentId);
        p.Add("ProjectId", request.ProjectId);
        p.Add("LocationId", request.LocationId);
        p.Add("CostCentreId", request.CostCentreId);
        p.Add("RequestDate", request.RequestDate);
        p.Add("RequiredDate", request.RequiredDate);
        p.Add("Priority", request.Priority.ToString());
        p.Add("Subject", request.Subject);
        p.Add("ApprovalStatus", request.ApprovalStatus.ToString());
        p.Add("CurrentStatus", request.CurrentStatus.ToString());
        p.Add("CurrentStepKey", request.CurrentStepKey);
        p.Add("CurrentStepSeq", request.CurrentStepSeq);
        p.Add("ResponsibleEmployeeId", request.ResponsibleEmployeeId);
        p.Add("ResponsibleRole", request.ResponsibleRole);
        p.Add("Remarks", request.Remarks);
        p.Add("PayloadJson", request.PayloadJson);
        p.Add("AmountMinor", null);
        p.Add("ParentRequestId", null);
        p.Add("ClosedUtc", request.ClosedUtc);

        var sql =
            "INSERT INTO requests (request_no, module_code, definition_id, definition_version, requester_employee_id, " +
            "department_id, project_id, location_id, cost_centre_id, request_date, required_date, priority, subject, " +
            "approval_status, current_status, current_step_key, current_step_seq, responsible_employee_id, " +
            "responsible_role, remarks, payload_json, amount_minor, parent_request_id, closed_utc, " +
            AuditSql.InsertColumns + ") VALUES (" +
            "@RequestNo, @ModuleCode, @DefinitionId, @DefinitionVersion, @RequesterEmployeeId, " +
            "@DepartmentId, @ProjectId, @LocationId, @CostCentreId, @RequestDate, @RequiredDate, @Priority, @Subject, " +
            "@ApprovalStatus, @CurrentStatus, @CurrentStepKey, @CurrentStepSeq, @ResponsibleEmployeeId, " +
            "@ResponsibleRole, @Remarks, @PayloadJson, @AmountMinor, @ParentRequestId, @ClosedUtc, " +
            AuditSql.InsertValues + ") RETURNING id";
        return await tx.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, tx, cancellationToken: ct));
    }

    public async Task<RequestSnapshot?> GetSnapshotAsync(DbTransaction tx, long id, CancellationToken ct)
    {
        var sql = "SELECT " + RequestColumns + " FROM requests r WHERE r.id = @Id AND " + AuditSql.Active("r");
        return await tx.Connection!.QuerySingleOrDefaultAsync<RequestSnapshot>(
            new CommandDefinition(sql, new { Id = id }, tx, cancellationToken: ct));
    }

    public async Task<int> UpdateRequestAsync(DbTransaction tx, RequestSnapshot request, long expectedRowVersion, CancellationToken ct)
    {
        // updated_utc and updated_by come from the stamper in the same statement as the version increment.
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", request.Id);
        p.Add("Expected", expectedRowVersion);
        p.Add("ApprovalStatus", request.ApprovalStatus.ToString());
        p.Add("CurrentStatus", request.CurrentStatus.ToString());
        p.Add("CurrentStepKey", request.CurrentStepKey);
        p.Add("CurrentStepSeq", request.CurrentStepSeq);
        p.Add("ResponsibleEmployeeId", request.ResponsibleEmployeeId);
        p.Add("ResponsibleRole", request.ResponsibleRole);
        p.Add("ClosedUtc", request.ClosedUtc);

        var sql =
            "UPDATE requests SET approval_status = @ApprovalStatus, current_status = @CurrentStatus, " +
            "current_step_key = @CurrentStepKey, current_step_seq = @CurrentStepSeq, " +
            "responsible_employee_id = @ResponsibleEmployeeId, responsible_role = @ResponsibleRole, " +
            "closed_utc = @ClosedUtc, row_version = row_version + 1, " + AuditSql.UpdateSet +
            " WHERE id = @Id AND row_version = @Expected AND " + AuditColumns.IsActive + " = 1 AND " +
            AuditColumns.DeletedUtc + " IS NULL";
        return await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, p, tx, cancellationToken: ct));
    }

    public async Task InsertStepsAsync(DbTransaction tx, long requestId, IReadOnlyList<RequestStepRow> steps, CancellationToken ct)
    {
        const string sql =
            "INSERT INTO request_steps (request_id, seq, step_key, name, step_type, state, activated_utc, " +
            "acted_by_user_id, acted_by_name, acted_utc, comment, captured_json) " +
            "VALUES (@RequestId, @Seq, @StepKey, @Name, @StepType, @State, @ActivatedUtc, " +
            "@ActedByUserId, @ActedByName, @ActedUtc, @Comment, @CapturedJson)";
        foreach (var step in steps)
        {
            await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, StepParameters(requestId, step), tx, cancellationToken: ct));
        }
    }

    public async Task UpdateStepAsync(DbTransaction tx, RequestStepRow step, CancellationToken ct)
    {
        const string sql =
            "UPDATE request_steps SET state = @State, activated_utc = @ActivatedUtc, acted_by_user_id = @ActedByUserId, " +
            "acted_by_name = @ActedByName, acted_utc = @ActedUtc, comment = @Comment, captured_json = @CapturedJson " +
            "WHERE request_id = @RequestId AND seq = @Seq";
        await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, StepParameters(step.RequestId, step), tx, cancellationToken: ct));
    }

    public async Task InsertActorsAsync(DbTransaction tx, long requestId, IReadOnlyList<ActorRow> actors, CancellationToken ct)
    {
        const string sql =
            "INSERT INTO request_step_actors (request_id, step_seq, role_name, employee_id) " +
            "VALUES (@RequestId, @StepSeq, @RoleName, @EmployeeId)";
        foreach (var actor in actors)
        {
            await tx.Connection!.ExecuteAsync(new CommandDefinition(
                sql, new { RequestId = requestId, actor.StepSeq, actor.RoleName, actor.EmployeeId }, tx, cancellationToken: ct));
        }
    }

    public async Task DeactivateActorsAsync(DbTransaction tx, long requestId, CancellationToken ct)
    {
        const string sql = "UPDATE request_step_actors SET is_active = 0 WHERE request_id = @RequestId AND is_active = 1";
        await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, new { RequestId = requestId }, tx, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RequestStepRow>> GetStepsAsync(DbConnection connection, long requestId, CancellationToken ct)
    {
        var sql = "SELECT " + StepColumns + " FROM request_steps s WHERE s.request_id = @RequestId ORDER BY s.seq";
        return (await connection.QueryAsync<RequestStepRow>(
            new CommandDefinition(sql, new { RequestId = requestId }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<ActorRow>> GetActiveActorsAsync(DbConnection connection, long requestId, CancellationToken ct)
    {
        const string sql =
            "SELECT a.id, a.request_id, a.step_seq, a.role_name, a.employee_id FROM request_step_actors a " +
            "WHERE a.request_id = @RequestId AND a.is_active = 1 ORDER BY a.step_seq, a.id";
        return (await connection.QueryAsync<ActorRow>(
            new CommandDefinition(sql, new { RequestId = requestId }, cancellationToken: ct))).ToList();
    }

    public async Task<bool> IsVisibleToAsync(long requestId, ActorContext actor, CancellationToken ct)
    {
        var privileged = actor.Roles.Contains(Roles.SystemAdmin) || actor.Roles.Contains(Roles.Admin);
        var sql =
            "SELECT EXISTS (SELECT 1 FROM requests r WHERE r.id = @Id AND " + AuditSql.Active("r") + " AND (" +
            "@Privileged = 1 " +
            "OR r.requester_employee_id = @EmployeeId " +
            "OR EXISTS (SELECT 1 FROM request_steps s WHERE s.request_id = r.id AND s.acted_by_user_id = @UserId) " +
            "OR EXISTS (SELECT 1 FROM request_step_actors a WHERE a.request_id = r.id AND a.is_active = 1 " +
            "AND (a.employee_id = @EmployeeId OR a.role_name IN @RoleNames))))";
        var parameters = new
        {
            Id = requestId,
            Privileged = privileged ? 1 : 0,
            EmployeeId = actor.EmployeeId,
            UserId = actor.UserId,
            RoleNames = actor.Roles.ToArray()
        };
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, parameters, cancellationToken: ct)) == 1;
    }

    private static DynamicParameters StepParameters(long requestId, RequestStepRow step)
    {
        var p = new DynamicParameters();
        p.Add("RequestId", requestId);
        p.Add("Seq", step.Seq);
        p.Add("StepKey", step.StepKey);
        p.Add("Name", step.Name);
        p.Add("StepType", step.StepType.ToString());
        p.Add("State", step.State.ToString());
        p.Add("ActivatedUtc", step.ActivatedUtc);
        p.Add("ActedByUserId", step.ActedByUserId);
        p.Add("ActedByName", step.ActedByName);
        p.Add("ActedUtc", step.ActedUtc);
        p.Add("Comment", step.Comment);
        p.Add("CapturedJson", NullWhenEmpty(step.CapturedJson));
        return p;
    }

    private static string? NullWhenEmpty(string? json) =>
        string.IsNullOrWhiteSpace(json) || json.Trim() == "{}" ? null : json;
}
