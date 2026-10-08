using System.Data.Common;
using System.Text.Json;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Application.Masters;
using AdminDesk.Domain.Definitions;
using AdminDesk.Domain.Engine;
using AdminDesk.SharedKernel.Actors;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Time;

namespace AdminDesk.Application.Engine;

// Creates requests and applies approve, reject, complete and cancel to them. Every public
// call runs in one write transaction: the request, its steps, its actor rows, the audit
// events and any hook work are saved together or not at all.
public sealed class RequestWorkflowService : IRequestWorkflowService
{
    // The only request status in which a step action is accepted.
    public static readonly IReadOnlySet<RequestStatus> ActionableStatuses =
        new HashSet<RequestStatus> { RequestStatus.InProgress };

    private const int ReasonLimit = 1000;
    private const string RequesterLabel = "Requester";
    private const string ReportingManagerLabel = "Reporting manager";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRequestRepository _requests;
    private readonly IAuditRepository _audit;
    private readonly IDefinitionProvider _definitions;
    private readonly ILimitRepository _limits;
    private readonly IEmployeeRepository _employees;
    private readonly DefinitionPayloadValidator _validator;
    private readonly SubjectRenderer _subjects;
    private readonly IEnumerable<IRequestHook> _hooks;
    private readonly IActorAccessor _actorAccessor;
    private readonly TimeProvider _clock;
    private readonly ICorrelationIdAccessor _correlation;

    public RequestWorkflowService(
        IUnitOfWork unitOfWork,
        IRequestRepository requests,
        IAuditRepository audit,
        IDefinitionProvider definitions,
        ILimitRepository limits,
        IEmployeeRepository employees,
        DefinitionPayloadValidator validator,
        SubjectRenderer subjects,
        IEnumerable<IRequestHook> hooks,
        IActorAccessor actorAccessor,
        TimeProvider clock,
        ICorrelationIdAccessor correlation)
    {
        _unitOfWork = unitOfWork;
        _requests = requests;
        _audit = audit;
        _definitions = definitions;
        _limits = limits;
        _employees = employees;
        _validator = validator;
        _subjects = subjects;
        _hooks = hooks;
        _actorAccessor = actorAccessor;
        _clock = clock;
        _correlation = correlation;
    }

    // ---------------------------------------------------------------- create

    public async Task<long> CreateAsync(ActorContext actor, CreateRequestCommand command, CancellationToken ct)
    {
        _actorAccessor.Use(actor.UserId);

        if (actor.EmployeeId is not { } requesterId)
        {
            throw new ForbiddenException("Your account is not linked to an employee profile.", ErrorCodes.NO_EMPLOYEE_PROFILE);
        }

        var issued = await ResolveDefinitionAsync(command, ct);
        var definition = issued.Definition;

        var employee = await _employees.GetByIdAsync(requesterId, ct);
        if (employee is null)
        {
            throw new ForbiddenException("Your account is not linked to an employee profile.", ErrorCodes.NO_EMPLOYEE_PROFILE);
        }
        var managerId = await _employees.GetReportingManagerIdAsync(requesterId, ct);
        var requester = new RequesterInfo(requesterId, managerId is { } m ? (int)m : null, actor.Roles);

        var today = IndiaTime.Today(_clock);
        var normalised = await _validator.ValidateAsync(definition, command.Common, today, command.Payload, ct);
        var limits = await LoadLimitsAsync(definition.Code, ct);

        var values = new FieldValues(normalised);
        var advanced = Advance(definition, StepPlanner.PlanInitial(definition), values, limits, requester);
        var now = _clock.GetUtcNow().UtcDateTime;
        var year = IndiaTime.Year(_clock);

        return await _unitOfWork.ExecuteInTransactionAsync(async (connection, tx) =>
        {
            // The statement behind ISqlDialect.UpsertCounterReturningSql runs here, in the
            // same transaction as the insert, so a failed create gives the number back.
            var number = await _requests.NextCounterAsync(tx, definition.Prefix, year, ct);

            var remarks = string.IsNullOrWhiteSpace(command.Common.Remarks) ? null : command.Common.Remarks.Trim();
            var snapshot = new RequestSnapshot
            {
                RequestNo = $"{definition.Prefix}-{year}-{number:D4}",
                ModuleCode = definition.Code,
                DefinitionId = checked((int)issued.Id),
                DefinitionVersion = definition.Version,
                RequesterEmployeeId = requesterId,
                DepartmentId = employee.DepartmentId is { } department ? (int)department : null,
                ProjectId = command.Common.ProjectId,
                LocationId = command.Common.LocationId,
                CostCentreId = command.Common.CostCentreId,
                RequestDate = today,
                RequiredDate = command.Common.RequiredDate,
                Priority = command.Common.Priority,
                Subject = _subjects.Render(definition, values),
                ApprovalStatus = advanced.Approval,
                CurrentStatus = advanced.Status,
                CurrentStepKey = advanced.Active?.Key,
                CurrentStepSeq = advanced.Active?.Seq,
                ResponsibleEmployeeId = advanced.ResponsibleEmployeeId,
                ResponsibleRole = advanced.ResponsibleRole,
                Remarks = remarks,
                PayloadJson = JsonSerializer.Serialize(normalised),
                RowVersion = 1,
                ClosedUtc = advanced.Finished ? now : null
            };

            var id = await _requests.InsertRequestAsync(tx, snapshot, ct);
            snapshot = snapshot with { Id = id };

            var rows = advanced.Steps.Select(step => new RequestStepRow
            {
                RequestId = id,
                Seq = step.Seq,
                StepKey = step.Key,
                Name = step.Name,
                StepType = step.Type,
                State = step.State,
                ActivatedUtc = advanced.Active?.Seq == step.Seq ? now : null
            }).ToList();
            await _requests.InsertStepsAsync(tx, id, rows, ct);
            if (advanced.Actors.Count > 0)
            {
                await _requests.InsertActorsAsync(tx, id, advanced.Actors, ct);
            }

            await AppendAuditAsync(tx, id, AuditEventTypes.Created, actor.UserId, actor.Name, RequesterLabel,
                null, null, RequestStatus.InProgress.ToString(), null, null, now, ct);

            var context = new HookContext(connection, tx, actor, snapshot);
            foreach (var hook in _hooks)
            {
                await hook.OnCreatingAsync(context, ct);
            }

            if (advanced.Finished)
            {
                await CloseAsync(connection, tx, actor, snapshot, now, ct);
            }

            return id;
        }, ct);
    }

    private async Task<IssuedDefinition> ResolveDefinitionAsync(CreateRequestCommand command, CancellationToken ct)
    {
        var stale = new ValidationException("definitionId", "This form is out of date. Reload the page and try again.");

        var issued = await _definitions.GetByIdAsync(command.DefinitionId, ct);
        if (issued is null || !string.Equals(issued.Definition.Code, command.ModuleCode, StringComparison.OrdinalIgnoreCase))
        {
            throw stale;
        }

        var active = await _definitions.GetActiveAsync(issued.Definition.Code, ct);
        if (active is null || issued.Definition.Version > active.Definition.Version)
        {
            throw stale;
        }
        return issued;
    }

    private async Task<LimitSet> LoadLimitsAsync(string moduleCode, CancellationToken ct)
    {
        var rows = await _limits.ListForModuleAsync(moduleCode, ct);
        return new LimitSet(rows.Select(r => new LimitEntry(r.StepKey, r.LimitKey, r.ValueMinor, r.Unit)));
    }

    // ------------------------------------------------------------------- act

    public async Task<long> ActAsync(ActorContext actor, long requestId, ActionCommand command, CancellationToken ct)
    {
        _actorAccessor.Use(actor.UserId);

        return await _unitOfWork.ExecuteInTransactionAsync(async (connection, tx) =>
        {
            var request = await _requests.GetSnapshotAsync(tx, requestId, ct)
                ?? throw new NotFoundException("Request not found.", ErrorCodes.NOT_FOUND);

            // Order of refusals: reason (400), stale version (409), then what the actor may do (403).
            string? reason = null;
            if (command.Action is RequestAction.Reject or RequestAction.Cancel)
            {
                reason = RequireReason(command);
            }
            if (request.RowVersion != command.ExpectedRowVersion)
            {
                throw new ConflictException("This request was changed by someone else. Reload and try again.", ErrorCodes.STATE_CONFLICT);
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            return command.Action switch
            {
                RequestAction.Cancel => await CancelAsync(connection, tx, actor, request, reason!, now, ct),
                RequestAction.Approve or RequestAction.Reject or RequestAction.Complete =>
                    await ActOnStepAsync(connection, tx, actor, request, command, reason, now, ct),
                _ => throw NotAllowed()
            };
        }, ct);
    }

    private static string RequireReason(ActionCommand command)
    {
        var reason = command.Comment?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            var message = command.Action == RequestAction.Cancel
                ? "Give a reason for cancelling."
                : "Enter a reason so the requester knows why.";
            throw new ValidationException("comment", message);
        }
        if (reason.Length > ReasonLimit)
        {
            throw new ValidationException("comment", "Keep the reason to 1000 characters or fewer.");
        }
        return reason;
    }

    private async Task<long> CancelAsync(
        DbConnection connection, DbTransaction tx, ActorContext actor, RequestSnapshot request, string reason,
        DateTime now, CancellationToken ct)
    {
        if (actor.EmployeeId != request.RequesterEmployeeId || !TransitionRules.CanCancel(request.CurrentStatus))
        {
            throw NotAllowed();
        }

        await _requests.DeactivateActorsAsync(tx, request.Id, ct);
        var updated = request with
        {
            CurrentStatus = RequestStatus.Cancelled,
            ResponsibleEmployeeId = null,
            ResponsibleRole = null
        };
        await PersistRequestAsync(tx, updated, request.RowVersion, ct);
        await AppendAuditAsync(tx, request.Id, AuditEventTypes.Cancelled, actor.UserId, actor.Name, RequesterLabel,
            request.CurrentStepKey, request.CurrentStatus.ToString(), RequestStatus.Cancelled.ToString(), reason, null, now, ct);

        await RunTerminalHooksAsync(connection, tx, actor, updated with { RowVersion = request.RowVersion + 1 }, RequestStatus.Cancelled, ct);
        return request.RowVersion + 1;
    }

    private async Task<long> ActOnStepAsync(
        DbConnection connection, DbTransaction tx, ActorContext actor, RequestSnapshot request, ActionCommand command,
        string? reason, DateTime now, CancellationToken ct)
    {
        if (!ActionableStatuses.Contains(request.CurrentStatus))
        {
            throw NotAllowed();
        }

        var issued = await _definitions.GetByIdAsync(request.DefinitionId, ct)
            ?? throw new InvalidOperationException($"Definition {request.DefinitionId} of request {request.Id} is missing.");
        var definition = issued.Definition;

        var rows = (await _requests.GetStepsAsync(tx, request.Id, ct)).ToList();
        var current = rows.FirstOrDefault(r => r.Seq == request.CurrentStepSeq && r.State == StepState.Pending);
        if (current is null || !TransitionRules.CanAct(current.StepType, command.Action))
        {
            throw NotAllowed();
        }

        var definitionStep = definition.Steps.First(s => s.Key == current.StepKey);
        var actors = await _requests.GetActiveActorsAsync(tx, request.Id, ct);
        var match = actors.FirstOrDefault(a => a.StepSeq == current.Seq && Matches(a, actor));
        if (match is null)
        {
            throw NotAllowed();
        }
        var actorRole = RoleLabel(match, definitionStep, request);

        IReadOnlyDictionary<string, object?>? captured = null;
        if (command.Action == RequestAction.Complete)
        {
            captured = await _validator.ValidateCapturedAsync(definitionStep.CaptureFields, command.Captured, ct);
        }

        var rejected = command.Action == RequestAction.Reject;
        var capturedJson = captured is { Count: > 0 } ? JsonSerializer.Serialize(captured) : null;
        var acted = current with
        {
            State = rejected ? StepState.Rejected : StepState.Done,
            ActedByUserId = actor.UserId,
            ActedByName = actor.Name,
            ActedUtc = now,
            Comment = rejected ? reason : null,
            CapturedJson = capturedJson
        };
        await _requests.UpdateStepAsync(tx, acted, ct);
        rows[rows.FindIndex(r => r.Seq == acted.Seq)] = acted;
        await _requests.DeactivateActorsAsync(tx, request.Id, ct);

        var newVersion = request.RowVersion + 1;

        if (rejected)
        {
            var rejectedRequest = request with
            {
                ApprovalStatus = ApprovalStatus.Rejected,
                CurrentStatus = RequestStatus.Rejected,
                ResponsibleEmployeeId = null,
                ResponsibleRole = null
            };
            await PersistRequestAsync(tx, rejectedRequest, request.RowVersion, ct);
            await AppendAuditAsync(tx, request.Id, AuditEventTypes.Rejected, actor.UserId, actor.Name, actorRole,
                current.StepKey, request.CurrentStatus.ToString(), RequestStatus.Rejected.ToString(), reason, null, now, ct);
            await RunTerminalHooksAsync(connection, tx, actor, rejectedRequest with { RowVersion = newVersion }, RequestStatus.Rejected, ct);
            return newVersion;
        }

        var eventType = command.Action == RequestAction.Approve ? AuditEventTypes.StepApproved : AuditEventTypes.StepCompleted;
        await AppendAuditAsync(tx, request.Id, eventType, actor.UserId, actor.Name, actorRole,
            current.StepKey, request.CurrentStatus.ToString(), request.CurrentStatus.ToString(), null, capturedJson, now, ct);

        var stepContext = new HookContext(connection, tx, actor, request);
        var info = new StepDoneInfo(current.StepKey, command.Action, captured ?? new Dictionary<string, object?>());
        foreach (var hook in _hooks)
        {
            await hook.OnStepDoneAsync(stepContext, info, ct);
        }

        var managerId = await _employees.GetReportingManagerIdAsync(request.RequesterEmployeeId, ct);
        var requester = new RequesterInfo(request.RequesterEmployeeId, managerId is { } m ? (int)m : null, new HashSet<string>());
        var limits = await LoadLimitsAsync(definition.Code, ct);
        var values = BuildValues(definition, request.PayloadJson, rows);
        var advanced = Advance(definition, rows.Select(ToPlanned).ToList(), values, limits, requester);

        // Save what the walk decided: steps that changed state and the step that became active.
        foreach (var step in advanced.Steps)
        {
            var row = rows.First(r => r.Seq == step.Seq);
            var activating = advanced.Active?.Seq == step.Seq && row.ActivatedUtc is null;
            if (row.State != step.State || activating)
            {
                var changed = row with { State = step.State, ActivatedUtc = activating ? now : row.ActivatedUtc };
                await _requests.UpdateStepAsync(tx, changed, ct);
            }
        }
        if (advanced.Actors.Count > 0)
        {
            await _requests.InsertActorsAsync(tx, request.Id, advanced.Actors, ct);
        }

        var updated = request with
        {
            ApprovalStatus = advanced.Approval,
            CurrentStatus = advanced.Status,
            CurrentStepKey = advanced.Active?.Key,
            CurrentStepSeq = advanced.Active?.Seq,
            ResponsibleEmployeeId = advanced.ResponsibleEmployeeId,
            ResponsibleRole = advanced.ResponsibleRole,
            ClosedUtc = advanced.Finished ? now : null
        };
        await PersistRequestAsync(tx, updated, request.RowVersion, ct);

        if (advanced.Finished)
        {
            await CloseAsync(connection, tx, actor, updated with { RowVersion = newVersion }, now, ct);
        }
        return newVersion;
    }

    // ---------------------------------------------------------------- helpers

    private static ForbiddenException NotAllowed() =>
        new("You cannot perform this action on this request.", ErrorCodes.ACTION_NOT_ALLOWED);

    private static bool Matches(ActorRow row, ActorContext actor) =>
        (row.EmployeeId is { } employeeId && actor.EmployeeId == employeeId)
        || (row.RoleName is { } role && actor.Roles.Contains(role));

    private static string RoleLabel(ActorRow match, StepDefinition step, RequestSnapshot request)
    {
        if (match.EmployeeId is { } employeeId)
        {
            return step.Actor?.Requester == true && employeeId == request.RequesterEmployeeId
                ? RequesterLabel
                : ReportingManagerLabel;
        }
        return match.RoleName ?? string.Empty;
    }

    private async Task PersistRequestAsync(DbTransaction tx, RequestSnapshot request, long expectedVersion, CancellationToken ct)
    {
        var affected = await _requests.UpdateRequestAsync(tx, request, expectedVersion, ct);
        if (affected == 0)
        {
            throw new ConflictException("This request was changed by someone else. Reload and try again.", ErrorCodes.STATE_CONFLICT);
        }
    }

    // Closing is automatic: it is recorded by the system in the same transaction as the last step.
    private async Task CloseAsync(
        DbConnection connection, DbTransaction tx, ActorContext actor, RequestSnapshot closed, DateTime now, CancellationToken ct)
    {
        await AppendAuditAsync(tx, closed.Id, AuditEventTypes.Closed, SystemActor.UserId, SystemActor.Name, null,
            null, RequestStatus.InProgress.ToString(), RequestStatus.Closed.ToString(), null, null, now, ct);
        await RunTerminalHooksAsync(connection, tx, actor, closed, RequestStatus.Closed, ct);
    }

    private async Task RunTerminalHooksAsync(
        DbConnection connection, DbTransaction tx, ActorContext actor, RequestSnapshot request, RequestStatus finalStatus,
        CancellationToken ct)
    {
        var context = new HookContext(connection, tx, actor, request);
        var info = new TerminalInfo(finalStatus);
        foreach (var hook in _hooks)
        {
            await hook.OnTerminalAsync(context, info, ct);
        }
    }

    private Task AppendAuditAsync(
        DbTransaction tx, long requestId, string eventType, string? userId, string name, string? role, string? stepKey,
        string? fromStatus, string? toStatus, string? comment, string? detailsJson, DateTime now, CancellationToken ct) =>
        _audit.AppendAsync(
            tx,
            new AuditEvent(requestId, eventType, userId, name, role, stepKey, fromStatus, toStatus, comment, detailsJson,
                _correlation.CorrelationId, now),
            ct);

    private static PlannedStep ToPlanned(RequestStepRow row) =>
        new(row.Seq, row.StepKey, row.Name, row.StepType, row.State, Array.Empty<ActorSlot>());

    private sealed record Advanced(
        IReadOnlyList<PlannedStep> Steps,
        PlannedStep? Active,
        bool Finished,
        IReadOnlyList<ActorRow> Actors,
        int? ResponsibleEmployeeId,
        string? ResponsibleRole,
        RequestStatus Status,
        ApprovalStatus Approval);

    // Decides the steps that can be decided now, finds the step waiting for action and who may
    // act on it. A step nobody can act on is still the active step: it waits.
    private static Advanced Advance(
        ModuleDefinition definition, IReadOnlyList<PlannedStep> steps, FieldValues values, LimitSet limits,
        RequesterInfo requester)
    {
        var forward = StepPlanner.ResolveForward(definition, steps, values, limits);
        var finished = StepPlanner.IsFinished(forward.Steps);
        var active = forward.ActiveSeq is { } seq ? forward.Steps.First(s => s.Seq == seq) : null;

        var actors = new List<ActorRow>();
        int? responsibleEmployee = null;
        string? responsibleRole = null;
        if (active is not null)
        {
            var definitionStep = definition.Steps.First(s => s.Key == active.Key);
            var slots = StepResolver.ResolveActors(definitionStep, requester);
            actors.AddRange(slots.Select(slot => new ActorRow { StepSeq = active.Seq, RoleName = slot.RoleName, EmployeeId = slot.EmployeeId }));

            responsibleEmployee = slots.FirstOrDefault(s => s.EmployeeId is not null)?.EmployeeId;
            if (responsibleEmployee is null)
            {
                var roles = slots.Where(s => s.RoleName is not null).Select(s => s.RoleName!).ToList();
                responsibleRole = roles.Count > 0
                    ? string.Join(" or ", roles)
                    : definitionStep.Actor?.ReportingManager == true ? ReportingManagerLabel : null;
            }
        }

        var (status, approval) = StatusDeriver.Derive(forward.Steps, finished);
        return new Advanced(forward.Steps, active, finished, actors, responsibleEmployee, responsibleRole, status, approval);
    }

    // The payload plus the captured values of finished steps (under stepKey.fieldKey), typed
    // the way the rule evaluator expects them.
    private static FieldValues BuildValues(ModuleDefinition definition, string payloadJson, IReadOnlyList<RequestStepRow> rows)
    {
        var all = new Dictionary<string, object?>();
        ReadTyped(definition.Fields, payloadJson, string.Empty, all);
        foreach (var row in rows.Where(r => r.State == StepState.Done && !string.IsNullOrEmpty(r.CapturedJson)))
        {
            var fields = definition.Steps.FirstOrDefault(s => s.Key == row.StepKey)?.CaptureFields;
            if (fields is not null)
            {
                ReadTyped(fields, row.CapturedJson!, row.StepKey + ".", all);
            }
        }
        return new FieldValues(all);
    }

    private static void ReadTyped(IReadOnlyList<FieldDefinition> fields, string json, string prefix, Dictionary<string, object?> into)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var field in fields)
        {
            if (!document.RootElement.TryGetProperty(field.Key, out var element) || element.ValueKind == JsonValueKind.Null)
            {
                continue;
            }
            into[prefix + field.Key] = field.Type switch
            {
                FieldType.Money or FieldType.Lookup => element.GetInt64(),
                FieldType.Number => element.GetDecimal(),
                FieldType.YesNo => element.GetBoolean(),
                FieldType.MultiSelect => element.EnumerateArray().Select(item => item.GetString()!).ToArray(),
                _ => element.GetString()
            };
        }
    }
}
