using System.Data.Common;
using System.Text.Json;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Application.Documents;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.Application.Requests;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Money;
using AdminDesk.SharedKernel.Time;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Requests;

// What the probe hook does when the workflow calls it.
public enum ProbeHookMode
{
    Off,
    Record,
    ThrowOnCreating,
    ThrowOnStepDone
}

public sealed record HookEvent(string Name, bool HasTransaction, long SeenInOwnTransaction, long SeenElsewhere, RequestStatus? FinalStatus);

// Shared by the probe and its test hook: the hook stays inert until the probe arms it.
public sealed class ProbeSwitch
{
    public const string FailureMessage = "probe hook failure";

    public ProbeHookMode Mode { get; set; } = ProbeHookMode.Off;

    public List<HookEvent> Events { get; } = new();
}

// Test-only hook used by the engine probe. It is registered only when the probe is switched on.
public sealed class ProbeRequestHook : IRequestHook
{
    private readonly ProbeSwitch _switch;
    private readonly IDbConnectionFactory _factory;

    public ProbeRequestHook(ProbeSwitch probeSwitch, IDbConnectionFactory factory)
    {
        _switch = probeSwitch;
        _factory = factory;
    }

    public async Task OnCreatingAsync(HookContext context, CancellationToken ct)
    {
        if (_switch.Mode == ProbeHookMode.ThrowOnCreating)
        {
            throw new InvalidOperationException(ProbeSwitch.FailureMessage);
        }
        if (_switch.Mode == ProbeHookMode.Record)
        {
            _switch.Events.Add(await ObserveAsync("creating", context, null, ct));
        }
    }

    public async Task OnStepDoneAsync(HookContext context, StepDoneInfo info, CancellationToken ct)
    {
        if (_switch.Mode == ProbeHookMode.ThrowOnStepDone)
        {
            throw new InvalidOperationException(ProbeSwitch.FailureMessage);
        }
        if (_switch.Mode == ProbeHookMode.Record)
        {
            _switch.Events.Add(await ObserveAsync("step-done", context, null, ct));
        }
    }

    public async Task OnTerminalAsync(HookContext context, TerminalInfo info, CancellationToken ct)
    {
        if (_switch.Mode == ProbeHookMode.Record)
        {
            _switch.Events.Add(await ObserveAsync("terminal", context, info.FinalStatus, ct));
        }
    }

    // Reads the request row through the hook's own transaction and through a separate connection.
    private async Task<HookEvent> ObserveAsync(string name, HookContext context, RequestStatus? final, CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM requests WHERE id = @Id";
        var own = await context.Connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, new { context.Request.Id }, context.Transaction, cancellationToken: ct));
        await using var other = await _factory.OpenAsync(ct);
        var elsewhere = await other.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { context.Request.Id }, cancellationToken: ct));
        return new HookEvent(name, context.Transaction is not null, own, elsewhere, final);
    }
}

public sealed class ProbeFailure : Exception
{
    public ProbeFailure(string message) : base(message)
    {
    }
}

// Opt-in end-to-end proof of the workflow engine. It creates and acts on many requests, so it is
// meant for a throwaway data folder only and is never switched on for a database that matters.
// Diagnostics:RunEngineProbe accepts false (skip), true (full probe) and pinning (the second run
// that proves a newer definition version leaves requests in flight alone).
public sealed class EngineProbeTask : IStartupTask
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;
    private readonly ILogger<EngineProbeTask> _logger;
    private readonly IHostEnvironment _environment;

    public EngineProbeTask(
        IConfiguration configuration, IServiceProvider services, ILogger<EngineProbeTask> logger, IHostEnvironment environment)
    {
        _environment = environment;
        _configuration = configuration;
        _services = services;
        _logger = logger;
    }

    public int Order => 900;

    public async Task RunAsync(CancellationToken ct)
    {
        var mode = _configuration[ConfigKeys.DiagnosticsRunEngineProbe]?.Trim().ToLowerInvariant();
        if (mode is not ("true" or "pinning"))
        {
            _logger.LogInformation("Engine probe skipped (Diagnostics:RunEngineProbe is false)");
            return;
        }

        // The probe creates requests and runs raw UPDATE statements, so it only runs on a development
        // machine with demo mode on; anywhere else the host refuses to start with it switched on.
        var demo = bool.TryParse(_configuration[ConfigKeys.DemoEnabled], out var demoEnabled) && demoEnabled;
        if (!_environment.IsDevelopment() || !demo)
        {
            throw new InvalidOperationException(
                $"{ConfigKeys.DiagnosticsRunEngineProbe} may only be switched on in the Development environment with {ConfigKeys.DemoEnabled} set to true.");
        }

        _logger.LogInformation("Engine probe running on this data folder; use a throwaway folder only (mode {Mode})", mode);
        var pinning = mode == "pinning";
        try
        {
            var kit = await ProbeKit.CreateAsync(_services, _logger, ct);
            if (pinning)
            {
                await new EngineProbeRoutingChecks(kit).RunPinningAsync(ct);
                _logger.LogInformation("ENGINE PROBE PINNING PASSED");
                return;
            }

            await new CoreChecks(kit).RunAsync(ct);
            await new EngineProbeRoutingChecks(kit).RunAsync(ct);
            await new EngineProbeDocumentChecks(kit).RunAsync(ct);
            await new EngineProbeConfigChecks(kit).RunAsync(ct);
            _logger.LogInformation("ENGINE PROBE PASSED ({Count} checks)", kit.Passed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, pinning ? "ENGINE PROBE PINNING FAILED: {Message}" : "ENGINE PROBE FAILED: {Message}", ex.Message);
            throw;
        }
    }
}

// Rows read back by the probe.
internal sealed class ProbeRequest
{
    public long Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string ApprovalStatus { get; set; } = string.Empty;
    public string? CurrentStepKey { get; set; }
    public int? CurrentStepSeq { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string? ResponsibleRole { get; set; }
    public long RowVersion { get; set; }
    public string? ClosedUtc { get; set; }
    public int DefinitionVersion { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public string CreatedUtc { get; set; } = string.Empty;
    public int IsActive { get; set; }
    public string? DeletedUtc { get; set; }
}

internal sealed class ProbeEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? UserId { get; set; }
}

internal sealed class ProbeStep
{
    public int Seq { get; set; }
    public string StepKey { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? ActivatedUtc { get; set; }
    public string? DueUtc { get; set; }
    public string? ActedByName { get; set; }
    public string? ActedUtc { get; set; }
    public string? Comment { get; set; }
    public string? CapturedJson { get; set; }
}

// Actors, helpers and assertions shared by both probe files.
internal sealed class ProbeKit
{
    public IRequestWorkflowService Service { get; private init; } = null!;
    public RequestAccessPolicy Access { get; private init; } = null!;
    public ILookupRegistry Lookups { get; private init; } = null!;
    public IDefinitionProvider Definitions { get; private init; } = null!;
    public IDbConnectionFactory Factory { get; private init; } = null!;
    public TimeProvider Clock { get; private init; } = null!;
    public ProbeSwitch Switch { get; private init; } = null!;
    public IDocumentService Documents { get; private init; } = null!;
    public IDocumentFileStore Files { get; private init; } = null!;
    public DocumentSettings DocumentLimits { get; private init; } = null!;
    public ILogger Logger { get; private init; } = null!;
    public IServiceProvider Services { get; private init; } = null!;

    public ActorContext Requester { get; private set; } = null!;
    public ActorContext Manager { get; private set; } = null!;
    public ActorContext Admin { get; private set; } = null!;
    public ActorContext SysAdmin { get; private set; } = null!;
    public ActorContext Hr { get; private set; } = null!;
    public ActorContext Store { get; private set; } = null!;
    public ActorContext Security { get; private set; } = null!;
    public ActorContext Management { get; private set; } = null!;
    public ActorContext Finance { get; private set; } = null!;
    public ActorContext Top { get; private set; } = null!;
    public ActorContext NoEmployee { get; private set; } = null!;
    public ActorContext Uninvolved { get; private set; } = null!;

    public int Passed { get; private set; }

    // Requests created by this run, and how many per request-number prefix.
    public List<long> Created { get; } = new();
    public Dictionary<string, int> CreatedByPrefix { get; } = new();

    // Courier requests the core checks leave at the proof of delivery step; the document checks finish them.
    public List<long> CourierAtProof { get; } = new();

    public static async Task<ProbeKit> CreateAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        var kit = new ProbeKit
        {
            Service = (IRequestWorkflowService)services.GetService(typeof(IRequestWorkflowService))!,
            Access = (RequestAccessPolicy)services.GetService(typeof(RequestAccessPolicy))!,
            Lookups = (ILookupRegistry)services.GetService(typeof(ILookupRegistry))!,
            Definitions = (IDefinitionProvider)services.GetService(typeof(IDefinitionProvider))!,
            Factory = (IDbConnectionFactory)services.GetService(typeof(IDbConnectionFactory))!,
            Clock = (TimeProvider)services.GetService(typeof(TimeProvider))!,
            Switch = (ProbeSwitch)services.GetService(typeof(ProbeSwitch))!,
            Documents = (IDocumentService)services.GetService(typeof(IDocumentService))!,
            Files = (IDocumentFileStore)services.GetService(typeof(IDocumentFileStore))!,
            DocumentLimits = (DocumentSettings)services.GetService(typeof(DocumentSettings))!,
            Logger = logger,
            Services = services
        };
        await kit.BuildActorsAsync(ct);
        return kit;
    }

    private async Task BuildActorsAsync(CancellationToken ct)
    {
        Requester = await ActorAsync("E0010", ct, Roles.Employee);
        Manager = await ActorAsync("E0009", ct, Roles.Employee);
        Admin = await ActorAsync("E0005", ct, Roles.Employee, Roles.Admin);
        SysAdmin = await ActorAsync("E0008", ct, Roles.Employee, Roles.SystemAdmin);
        Hr = await ActorAsync("E0003", ct, Roles.Employee, Roles.HR);
        Store = await ActorAsync("E0006", ct, Roles.Employee, Roles.Store);
        Security = await ActorAsync("E0007", ct, Roles.Employee, Roles.Security);
        Management = await ActorAsync("E0001", ct, Roles.Employee, Roles.Management);
        Finance = await ActorAsync("E0002", ct, Roles.Employee, Roles.Finance);
        Uninvolved = await ActorAsync("E0003", ct, Roles.Employee);
        Top = await ActorAsync(DemoAccountCatalogTop, ct, Roles.Employee);
        NoEmployee = new ActorContext("probe-no-employee", "Probe System Admin", null, new HashSet<string> { Roles.SystemAdmin });
    }

    private const string DemoAccountCatalogTop = "E0011";

    private async Task<ActorContext> ActorAsync(string code, CancellationToken ct, params string[] roles)
    {
        await using var connection = await Factory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<ProbeEmployee>(new CommandDefinition(
            "SELECT e.id AS Id, e.full_name AS Name, u.Id AS UserId FROM employees e " +
            "LEFT JOIN AspNetUsers u ON u.EmployeeId = e.id WHERE e.employee_code = @Code",
            new { Code = code }, cancellationToken: ct));
        if (row is null)
        {
            throw new ProbeFailure($"Employee {code} is missing; run the probe with Demo__Enabled=true.");
        }
        // The top of the hierarchy has no login, so it gets a fixed probe id.
        var userId = row.UserId ?? $"probe-{code}";
        return new ActorContext(userId, row.Name, row.Id, new HashSet<string>(roles));
    }

    public void Pass(string name)
    {
        Passed++;
        Logger.LogInformation("Engine probe check {Number} passed: {Name}", Passed, name);
    }

    public ProbeFailure Fail(string message) => new(message);

    public void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new ProbeFailure(message);
        }
    }

    public static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    public static Dictionary<string, JsonElement> ToPayload(object payload)
    {
        var element = JsonSerializer.SerializeToElement(payload);
        return element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    // ---------------------------------------------------------------- database

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await Factory.OpenAsync(default);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new ProbeFailure($"No value for: {sql}");
    }

    public async Task<T?> ScalarOrNullAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await Factory.OpenAsync(default);
        return await connection.ExecuteScalarAsync<T?>(sql, parameters);
    }

    public async Task<List<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await Factory.OpenAsync(default);
        return (await connection.QueryAsync<T>(sql, parameters)).ToList();
    }

    public async Task ExecuteRawAsync(string sql)
    {
        await using var connection = await Factory.OpenAsync(default);
        await connection.ExecuteAsync(sql);
    }

    public async Task<ProbeRequest> RequestAsync(long id)
    {
        var rows = await QueryAsync<ProbeRequest>(
            "SELECT id, request_no, current_status, approval_status, current_step_key, current_step_seq, " +
            "responsible_employee_id, responsible_role, row_version, closed_utc, definition_version, created_by, " +
            "updated_by, created_utc, is_active, deleted_utc FROM requests WHERE id = @Id", new { Id = id });
        return rows.SingleOrDefault() ?? throw new ProbeFailure($"Request {id} not found.");
    }

    public Task<List<ProbeStep>> StepsAsync(long id) =>
        QueryAsync<ProbeStep>(
            "SELECT seq, step_key, step_type, state, activated_utc, due_utc, acted_by_name, acted_utc, comment, captured_json " +
            "FROM request_steps WHERE request_id = @Id ORDER BY seq", new { Id = id });

    public async Task<ProbeStep> StepAsync(long id, string key) =>
        (await StepsAsync(id)).SingleOrDefault(s => s.StepKey == key) ?? throw new ProbeFailure($"Step {key} of request {id} not found.");

    public Task<long> ActiveActorCountAsync(long id) =>
        ScalarAsync<long>("SELECT COUNT(*) FROM request_step_actors WHERE request_id = @Id AND is_active = 1", new { Id = id });

    public Task<long> AuditCountAsync(long id, string? type = null) =>
        ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND (@Type IS NULL OR event_type = @Type)",
            new { Id = id, Type = type });

    public async Task<string> FingerprintAsync(long id)
    {
        var request = await RequestAsync(id);
        var steps = await StepsAsync(id);
        var actors = await ActiveActorCountAsync(id);
        var audits = await AuditCountAsync(id);
        return $"{request.RowVersion}|{request.CurrentStatus}|{request.ApprovalStatus}|{request.CurrentStepKey}|" +
               $"{string.Join(",", steps.Select(s => s.State))}|{actors}|{audits}";
    }

    public async Task<long> CounterAsync(string prefix) =>
        await ScalarOrNullAsync<long?>(
            "SELECT last_value FROM request_counters WHERE module_code = @Prefix AND year = @Year",
            new { Prefix = prefix, Year = IndiaTime.Year(Clock) }) ?? 0L;

    public Task<int> EmployeeIdAsync(string code) =>
        ScalarAsync<int>("SELECT id FROM employees WHERE employee_code = @Code", new { Code = code });

    // ---------------------------------------------------------------- engine calls

    public async Task<long> CreateAsync(ActorContext actor, string moduleCode, object payload, CommonFields? common = null, int? definitionId = null)
    {
        var definition = await Definitions.GetActiveAsync(moduleCode)
            ?? throw new ProbeFailure($"Definition {moduleCode} is not active.");
        var id = await Service.CreateAsync(actor, new CreateRequestCommand
        {
            ModuleCode = moduleCode,
            DefinitionId = definitionId ?? checked((int)definition.Id),
            Common = common ?? new CommonFields(),
            Payload = payload as Dictionary<string, JsonElement> ?? ToPayload(payload)
        }, default);
        Created.Add(id);
        var prefix = definition.Definition.Prefix;
        CreatedByPrefix[prefix] = CreatedByPrefix.GetValueOrDefault(prefix) + 1;
        return id;
    }

    // Acts with the request's current row version read just before the call.
    public async Task<long> ActAsync(ActorContext actor, long id, RequestAction action, string? comment = null,
        Dictionary<string, JsonElement>? captured = null)
    {
        var version = (await RequestAsync(id)).RowVersion;
        return await Service.ActAsync(actor, id, new ActionCommand
        {
            Action = action,
            Comment = comment,
            Captured = captured,
            ExpectedRowVersion = version
        }, default);
    }

    public async Task<TException> ExpectAsync<TException>(string what, Func<Task> call) where TException : Exception
    {
        try
        {
            await call();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new ProbeFailure($"{what}: expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}");
        }
        throw new ProbeFailure($"{what}: expected {typeof(TException).Name} but nothing was raised");
    }

    public async Task ExpectNotAllowedAsync(string what, Func<Task> call)
    {
        var ex = await ExpectAsync<ForbiddenException>(what, call);
        Check(ex.Code == ErrorCodes.ACTION_NOT_ALLOWED, $"{what}: expected code ACTION_NOT_ALLOWED but got {ex.Code}");
    }

    // Refused as not allowed when the actor can see the request, as not found when they cannot.
    public async Task ExpectRefusedAsync(string what, ActorContext actor, long id, Func<Task> call)
    {
        if (await Access.CanViewAsync(actor, id, default))
        {
            await ExpectNotAllowedAsync(what, call);
        }
        else
        {
            await ExpectHiddenAsync(what, call);
        }
    }

    // A request the actor may not see answers exactly as a missing one does, whatever the action.
    public async Task ExpectHiddenAsync(string what, Func<Task> call) =>
        await ExpectAsync<NotFoundException>(what, call);

    // The cancel is refused with CANCEL_LOCKED and nothing about the request changes.
    public async Task ExpectCancelLockedAsync(string what, ActorContext actor, long id, string reason)
    {
        var before = await RequestAsync(id);
        var actorsBefore = await ActiveActorCountAsync(id);
        var cancelledEventsBefore = await ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Cancelled'", new { Id = id });
        var ex = await ExpectAsync<DomainRuleException>(what, () => ActAsync(actor, id, RequestAction.Cancel, reason));
        Check(ex.Code == ErrorCodes.CANCEL_LOCKED, $"{what}: expected code CANCEL_LOCKED but got {ex.Code}");
        var after = await RequestAsync(id);
        Check(after.CurrentStatus == before.CurrentStatus && after.RowVersion == before.RowVersion && after.CurrentStepKey == before.CurrentStepKey,
            $"{what}: the refused cancel changed the request");
        Check(await ActiveActorCountAsync(id) == actorsBefore, $"{what}: the refused cancel changed the active actors");
        Check(await ScalarAsync<long>("SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Cancelled'", new { Id = id }) == cancelledEventsBefore,
            $"{what}: the refused cancel wrote an audit event");
    }

    public async Task ExpectFieldAsync(string what, string field, Func<Task> call)
    {
        var ex = await ExpectAsync<ValidationException>(what, call);
        Check(ex.FieldErrors.Any(e => e.Field == field), $"{what}: no error keyed to '{field}' (got {string.Join(", ", ex.FieldErrors.Select(e => e.Field))})");
    }

    // Moves a stationery request from where it is to the given step using the right actors.
    public async Task AdvanceStationeryAsync(long id, string untilStep)
    {
        var order = new Dictionary<string, Func<Task>>
        {
            ["manager-approval"] = () => ActAsync(Manager, id, RequestAction.Approve),
            ["verification"] = () => ActAsync(Store, id, RequestAction.Approve),
            ["stock-check"] = () => ActAsync(Store, id, RequestAction.Complete),
            ["issue"] = () => ActAsync(Store, id, RequestAction.Complete),
            ["acknowledgement"] = () => ActAsync(Requester, id, RequestAction.Complete),
            ["stock-update"] = () => ActAsync(Store, id, RequestAction.Complete)
        };
        while (true)
        {
            var current = (await RequestAsync(id)).CurrentStepKey;
            if (current is null || current == untilStep)
            {
                return;
            }
            await order[current]();
        }
    }
}

// Checks 1 to 19: Stationery, Courier, the field-type sample and the cross-cutting rules.
internal sealed class CoreChecks
{
    private readonly ProbeKit _k;

    public CoreChecks(ProbeKit kit)
    {
        _k = kit;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var year = IndiaTime.Year(_k.Clock);
        var n0 = await _k.CounterAsync("STN");
        var stationery = new { item = "Pencil", quantity = 5 };

        // 1 Stationery happy path
        var a = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        var ra = await _k.RequestAsync(a);
        _k.Check(ra.CurrentStatus == "InProgress" && ra.ApprovalStatus == "Pending", "1: new request is not InProgress/Pending");
        _k.Check(ra.CurrentStepKey == "manager-approval", "1: first step is not manager-approval");
        _k.Check(ra.ResponsibleEmployeeId == await _k.EmployeeIdAsync("E0009"), "1: responsible person is not the manager");
        await _k.ActAsync(_k.Manager, a, RequestAction.Approve);
        ra = await _k.RequestAsync(a);
        _k.Check(ra.CurrentStatus == "InProgress" && ra.ApprovalStatus == "Pending" && ra.CurrentStepKey == "verification", "1: after the manager the request is not at verification/Pending");
        await _k.ActAsync(_k.Store, a, RequestAction.Approve);
        ra = await _k.RequestAsync(a);
        _k.Check(ra.ApprovalStatus == "Approved" && ra.CurrentStepKey == "stock-check", "1: after verification approval is not Approved at stock-check");
        await _k.ActAsync(_k.Store, a, RequestAction.Complete);
        await _k.ActAsync(_k.Store, a, RequestAction.Complete);
        ra = await _k.RequestAsync(a);
        _k.Check(ra.CurrentStepKey == "acknowledgement" && ra.ResponsibleEmployeeId == _k.Requester.EmployeeId, "1: acknowledgement is not waiting for the requester");
        await _k.ActAsync(_k.Requester, a, RequestAction.Complete);
        ra = await _k.RequestAsync(a);
        _k.Check(ra.CurrentStatus == "InProgress" && ra.CurrentStepKey == "stock-update" && ra.ResponsibleRole == "Store", "1: request did not move to stock-update");
        var ack = await _k.StepAsync(a, "acknowledgement");
        _k.Check(ack.State == "Done" && ack.ActedByName == "Priya Nair" && ack.ActedUtc is not null, "1: acknowledgement did not record the name and time");
        var ackEvents = await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'StepCompleted' AND step_key = 'acknowledgement' AND actor_name = 'Priya Nair'",
            new { Id = a });
        _k.Check(ackEvents == 1, "1: acknowledgement is missing from the audit trail");
        var stockUpdateVersion = (await _k.RequestAsync(a)).RowVersion;
        await _k.ActAsync(_k.Store, a, RequestAction.Complete);
        ra = await _k.RequestAsync(a);
        _k.Check(ra.CurrentStatus == "Closed" && ra.ClosedUtc is not null && ra.ApprovalStatus == "Approved" && ra.RowVersion == stockUpdateVersion + 1,
            "1: request did not close after the last step");
        _k.Check(ra.RequestNo == $"STN-{year}-{n0 + 1:D4}", $"1: unexpected request number {ra.RequestNo}");

        // 2 permission, 3 reasons, 4 reject path (request b)
        var b = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        _k.Check((await _k.RequestAsync(b)).RequestNo == $"STN-{year}-{n0 + 2:D4}", "1: second request number is not the next in line");
        _k.Pass("1 stationery happy path");

        await _k.ExpectNotAllowedAsync("2: admin approving a manager step", () => _k.ActAsync(_k.Admin, b, RequestAction.Approve));
        _k.Pass("2 permission");

        var before = await _k.FingerprintAsync(b);
        foreach (var reason in new string?[] { null, "", "   ", new string('x', 1001) })
        {
            await _k.ExpectFieldAsync("3: reject reason", "comment", () => _k.ActAsync(_k.Manager, b, RequestAction.Reject, reason));
            await _k.ExpectFieldAsync("3: cancel reason", "comment", () => _k.ActAsync(_k.Requester, b, RequestAction.Cancel, reason));
        }
        _k.Check(before == await _k.FingerprintAsync(b), "3: a refused reason changed the request");
        await _k.ActAsync(_k.Manager, b, RequestAction.Reject, "no");
        _k.Pass("3 reasons");

        var rb = await _k.RequestAsync(b);
        var rejectedStep = await _k.StepAsync(b, "manager-approval");
        _k.Check(rb.CurrentStatus == "Rejected" && rb.ApprovalStatus == "Rejected", "4: request is not Rejected/Rejected");
        _k.Check(rejectedStep.State == "Rejected" && rejectedStep.Comment == "no", "4: rejected step does not carry the reason");
        _k.Check(await _k.ScalarAsync<long>("SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Rejected' AND comment = 'no'", new { Id = b }) == 1,
            "4: rejected audit event does not carry the reason");
        await _k.ExpectNotAllowedAsync("4: approve after reject", () => _k.ActAsync(_k.Manager, b, RequestAction.Approve));
        await _k.ExpectNotAllowedAsync("4: reject again", () => _k.ActAsync(_k.Manager, b, RequestAction.Reject, "again"));
        _k.Check(await _k.ActiveActorCountAsync(b) == 0, "4: a rejected request still has active actors");
        _k.Pass("4 reject path");

        // 5 pairing and 6 no notes (request c); 7 cancel at the last step
        var c = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        await _k.ExpectNotAllowedAsync("5: complete at an approval step", () => _k.ActAsync(_k.Manager, c, RequestAction.Complete));
        await _k.ActAsync(_k.Manager, c, RequestAction.Approve, "noted by manager");
        var managerStep = await _k.StepAsync(c, "manager-approval");
        _k.Check(managerStep.Comment is null, "6: an approval stored a note");
        _k.Check(await _k.ScalarAsync<long>("SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'StepApproved' AND comment IS NOT NULL", new { Id = c }) == 0,
            "6: an approval audit event carries a note");
        await _k.ActAsync(_k.Store, c, RequestAction.Approve);
        await _k.ExpectNotAllowedAsync("5: approve at a task", () => _k.ActAsync(_k.Store, c, RequestAction.Approve));
        await _k.ExpectNotAllowedAsync("5: reject at a task", () => _k.ActAsync(_k.Store, c, RequestAction.Reject, "no"));
        await _k.ActAsync(_k.Store, c, RequestAction.Complete, "task note");
        _k.Check((await _k.StepAsync(c, "stock-check")).Comment is null, "6: a task completion stored a note");
        _k.Check(await _k.ScalarAsync<long>("SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'StepCompleted' AND comment IS NOT NULL", new { Id = c }) == 0,
            "6: a task audit event carries a note");
        _k.Pass("6 no notes");

        await _k.ActAsync(_k.Store, c, RequestAction.Complete);
        await _k.ExpectNotAllowedAsync("5: approve the receipt confirmation", () => _k.ActAsync(_k.Requester, c, RequestAction.Approve));
        await _k.ExpectNotAllowedAsync("5: reject the receipt confirmation", () => _k.ActAsync(_k.Requester, c, RequestAction.Reject, "no"));
        await _k.ActAsync(_k.Requester, c, RequestAction.Complete);
        _k.Check((await _k.RequestAsync(c)).CurrentStepKey == "stock-update", "5: receipt confirmation did not move on");

        // Complete at a Stationery task with a captured key is refused (request c is at stock-update, a task).
        await _k.ExpectAsync<ValidationException>("12: undeclared captured key",
            () => _k.ActAsync(_k.Store, c, RequestAction.Complete, null, new Dictionary<string, JsonElement> { ["extra"] = ProbeKit.Json("x") }));

        // 7 cancel is locked once the material is issued (request c has passed the issue step)
        await _k.ExpectHiddenAsync("7: cancel by someone who cannot see the request", () => _k.ActAsync(_k.Finance, c, RequestAction.Cancel, "not mine"));
        await _k.ExpectHiddenAsync("7: reject without a reason by someone who cannot see the request", () => _k.ActAsync(_k.Finance, c, RequestAction.Reject));
        await _k.ExpectCancelLockedAsync("7: cancel after the material was issued", _k.Requester, c, "no longer needed");
        _k.Check((await _k.RequestAsync(c)).CurrentStatus == "InProgress", "7: the locked request is not in progress");
        await _k.ActAsync(_k.Store, c, RequestAction.Complete);
        _k.Check((await _k.RequestAsync(c)).CurrentStatus == "Closed", "7: the locked request could not be finished");

        // Cancel at the issue step itself is still allowed (the step is current, not done).
        var issueOnly = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        await _k.AdvanceStationeryAsync(issueOnly, "issue");
        _k.Check((await _k.RequestAsync(issueOnly)).CurrentStepKey == "issue", "7: could not reach the issue step");
        await _k.ActAsync(_k.Requester, issueOnly, RequestAction.Cancel, "changed my mind");
        var rIssue = await _k.RequestAsync(issueOnly);
        _k.Check(rIssue.CurrentStatus == "Cancelled" && await _k.ActiveActorCountAsync(issueOnly) == 0, "7: cancel at the issue step failed");

        var f = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        await _k.ActAsync(_k.Requester, f, RequestAction.Cancel, "  wrong item  ");
        var rf = await _k.RequestAsync(f);
        _k.Check(rf.CurrentStatus == "Cancelled" && rf.ApprovalStatus == "Pending" && await _k.ActiveActorCountAsync(f) == 0, "7: cancel at the first step failed");
        _k.Check(await _k.ScalarAsync<long>("SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Cancelled' AND comment = 'wrong item'", new { Id = f }) == 1,
            "7: cancel reason was not trimmed into the audit event");

        var g = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        await _k.AdvanceStationeryAsync(g, "stock-check");
        _k.Check((await _k.RequestAsync(g)).CurrentStepKey == "stock-check", "7: could not reach stock-check");
        await _k.ExpectAsync<ValidationException>("12: stationery task with a captured key",
            () => _k.ActAsync(_k.Store, g, RequestAction.Complete, null, new Dictionary<string, JsonElement> { ["extra"] = ProbeKit.Json(1) }));
        await _k.ActAsync(_k.Requester, g, RequestAction.Cancel, "stock not needed");
        _k.Check((await _k.RequestAsync(g)).CurrentStatus == "Cancelled", "7: cancel at a task step failed");

        await _k.ExpectNotAllowedAsync("7: cancel a closed request", () => _k.ActAsync(_k.Requester, a, RequestAction.Cancel, "too late"));
        await _k.ExpectNotAllowedAsync("7: cancel a rejected request", () => _k.ActAsync(_k.Requester, b, RequestAction.Cancel, "too late"));
        _k.Pass("5 action and step pairing");
        _k.Pass("7 cancel until the material is issued");

        // 8 no self-skip
        var h = await _k.CreateAsync(_k.Store, "stationery", stationery);
        var rh = await _k.RequestAsync(h);
        _k.Check(rh.CurrentStepKey == "manager-approval" && rh.ResponsibleEmployeeId == _k.Admin.EmployeeId, "8: the store person's manager step is not for the admin");
        await _k.ActAsync(_k.Admin, h, RequestAction.Approve);
        rh = await _k.RequestAsync(h);
        var verification = await _k.StepAsync(h, "verification");
        _k.Check(rh.CurrentStepKey == "verification" && verification.State == "Pending" && rh.ResponsibleRole == "Admin or Store",
            "8: verification was not left waiting for Admin or Store");
        await _k.ExpectNotAllowedAsync("8: the requester approving her own request", () => _k.ActAsync(_k.Store, h, RequestAction.Approve));
        await _k.ExpectNotAllowedAsync("8: the requester rejecting her own request", () => _k.ActAsync(_k.Store, h, RequestAction.Reject, "no"));
        await _k.ActAsync(_k.Admin, h, RequestAction.Approve);
        _k.Check((await _k.RequestAsync(h)).CurrentStepKey == "stock-check", "8: another holder of the role could not approve");
        await _k.ActAsync(_k.Store, h, RequestAction.Cancel, "test done");
        _k.Pass("8 no self-skip");

        // 9 a step nobody can act on waits
        var i = await _k.CreateAsync(_k.Top, "stationery", stationery);
        var ri = await _k.RequestAsync(i);
        _k.Check(ri.CurrentStatus == "InProgress" && ri.ApprovalStatus == "Pending" && ri.CurrentStepKey == "manager-approval", "9: top-of-tree request is not waiting at manager-approval");
        _k.Check(ri.ResponsibleRole == "Reporting manager" && ri.ResponsibleEmployeeId is null, "9: responsible label is not 'Reporting manager'");
        _k.Check(await _k.ActiveActorCountAsync(i) == 0, "9: the waiting step has an actor row");
        await _k.ExpectNotAllowedAsync("9: admin approving a step nobody holds", () => _k.ActAsync(_k.Admin, i, RequestAction.Approve));
        await _k.ActAsync(_k.Top, i, RequestAction.Cancel, "no manager");
        _k.Check((await _k.RequestAsync(i)).CurrentStatus == "Cancelled", "9: the requester could not cancel a waiting request");
        _k.Pass("9 a step nobody can act on waits");

        // 10 conflict, 19 stamps (request j)
        var j = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        var jVersion = (await _k.RequestAsync(j)).RowVersion;
        var approve = new ActionCommand { Action = RequestAction.Approve, ExpectedRowVersion = jVersion };
        await _k.Service.ActAsync(_k.Manager, j, approve, ct);
        await _k.ExpectAsync<ConflictException>("10: acting twice with one row version", () => _k.Service.ActAsync(_k.Manager, j, approve, ct));
        _k.Pass("10 conflict");

        var rj = await _k.RequestAsync(j);
        _k.Check(rj.CreatedBy == _k.Requester.UserId && rj.UpdatedBy == _k.Manager.UserId, "19: created_by/updated_by are not the acting users");
        _k.Check(rj.CreatedUtc.Length > 0 && rj.IsActive == 1 && rj.DeletedUtc is null, "19: standard columns are not set");
        _k.Check(await _k.ScalarAsync<long>("SELECT COUNT(*) FROM employees WHERE created_by <> @System", new { System = SystemActor.UserId }) == 0,
            "19: seeded employees are not stamped with the system actor");
        await _k.ActAsync(_k.Requester, j, RequestAction.Cancel, "done with stamps");
        _k.Pass("19 audit stamps");

        // 11 Courier, 12 captured values
        await RunCourierAsync();

        // 13 audit
        var types = await _k.QueryAsync<string>("SELECT DISTINCT event_type FROM audit_events");
        var required = new[] { "Created", "StepApproved", "StepCompleted", "Rejected", "Cancelled", "Closed", "StepActivated" };
        var allowed = required.Append("StepSkipped").ToArray();
        _k.Check(types.All(allowed.Contains) && required.All(types.Contains), $"13: audit event types are {string.Join(",", types)}");
        var ex = await _k.ExpectAsync<SqliteException>("13: updating the audit trail",
            () => _k.ExecuteRawAsync("UPDATE audit_events SET comment = 'x'"));
        _k.Check(ex.SqliteErrorCode == 19, $"13: audit update failed with code {ex.SqliteErrorCode}, expected 19");
        _k.Pass("13 audit");

        // 14 counters (checked again at the end of the full run, here for the requests above)
        var stnCreated = _k.CreatedByPrefix.GetValueOrDefault("STN");
        _k.Check(await _k.CounterAsync("STN") == n0 + stnCreated, "14: STN counter does not match the requests created");
        _k.Pass("14 counters");

        await RunFieldTypesAsync();
        await RunOwnershipAndVisibilityAsync(a, b);
    }

    private async Task RunCourierAsync()
    {
        var courier = new
        {
            documentDescription = "Agreement",
            senderName = "Priya Nair",
            receiverName = "Receiver One",
            receiverAddress = "1 Example Road",
            receiverCity = "Pune"
        };
        var d = await _k.CreateAsync(_k.Requester, "courier", courier);
        var rd = await _k.RequestAsync(d);
        var steps = await _k.StepsAsync(d);
        _k.Check(rd.RequestNo.StartsWith("CUR-"), "11: request number does not start with CUR-");
        _k.Check(steps.Select(s => s.StepKey).SequenceEqual(new[] { "courier-selection", "dispatch", "tracking-number", "delivery-confirmation", "pod-upload" }), "11: unexpected courier steps");
        _k.Check(steps.All(s => s.StepType == "Task" && s.State != "NotRequired"), "11: courier steps are not all required tasks");
        _k.Check(rd.ApprovalStatus == "Approved" && rd.CurrentStepKey == "courier-selection" && rd.ResponsibleRole == "Admin", "11: courier start state is wrong");

        await _k.ExpectFieldAsync("11: courier selection without a company", "courierCompany", () => _k.ActAsync(_k.Admin, d, RequestAction.Complete));
        await _k.ExpectFieldAsync("12: courier company as a number", "courierCompany",
            () => _k.ActAsync(_k.Admin, d, RequestAction.Complete, null, new() { ["courierCompany"] = ProbeKit.Json(123) }));
        await _k.ExpectFieldAsync("12: courier company too long", "courierCompany",
            () => _k.ActAsync(_k.Admin, d, RequestAction.Complete, null, new() { ["courierCompany"] = ProbeKit.Json(new string('c', 81)) }));
        await _k.ActAsync(_k.Admin, d, RequestAction.Complete, null, new() { ["courierCompany"] = ProbeKit.Json("Example Couriers") });
        _k.Check((await _k.RequestAsync(d)).CurrentStepKey == "dispatch", "11: courier selection did not move to dispatch");
        await _k.ActAsync(_k.Admin, d, RequestAction.Complete);
        await _k.ExpectFieldAsync("11: tracking number missing", "trackingNumber", () => _k.ActAsync(_k.Admin, d, RequestAction.Complete));
        await _k.ExpectFieldAsync("11: tracking number blank", "trackingNumber",
            () => _k.ActAsync(_k.Admin, d, RequestAction.Complete, null, new() { ["trackingNumber"] = ProbeKit.Json("   ") }));
        await _k.ActAsync(_k.Admin, d, RequestAction.Complete, null, new() { ["trackingNumber"] = ProbeKit.Json("TRK123456") });
        rd = await _k.RequestAsync(d);
        _k.Check(rd.CurrentStepKey == "delivery-confirmation" && rd.ResponsibleEmployeeId == _k.Requester.EmployeeId, "11: delivery confirmation is not waiting for the requester");
        await _k.ExpectNotAllowedAsync("5: approve the delivery confirmation", () => _k.ActAsync(_k.Requester, d, RequestAction.Approve));
        await _k.ExpectNotAllowedAsync("5: reject the delivery confirmation", () => _k.ActAsync(_k.Requester, d, RequestAction.Reject, "no"));
        await _k.ActAsync(_k.Requester, d, RequestAction.Complete);
        // The proof of delivery step is the last one: the request stays open for the Admin until a document is uploaded.
        rd = await _k.RequestAsync(d);
        _k.Check(rd.CurrentStatus == "InProgress" && rd.CurrentStepKey == "pod-upload" && rd.ResponsibleRole == "Admin" && rd.ClosedUtc is null,
            "11: courier request is not waiting for the Admin at pod-upload after the delivery confirmation");
        _k.CourierAtProof.Add(d);
        _k.Pass("11 courier on the same engine");

        var selection = await _k.StepAsync(d, "courier-selection");
        var tracking = await _k.StepAsync(d, "tracking-number");
        _k.Check(selection.CapturedJson == "{\"courierCompany\":\"Example Couriers\"}", $"12: courier-selection captured {selection.CapturedJson}");
        _k.Check(tracking.CapturedJson == "{\"trackingNumber\":\"TRK123456\"}", $"12: tracking-number captured {tracking.CapturedJson}");
        _k.Check((await _k.StepAsync(d, "dispatch")).CapturedJson is null && (await _k.StepAsync(d, "delivery-confirmation")).CapturedJson is null, "12: steps without capture fields stored values");
        var details = await _k.QueryAsync<string>(
            "SELECT details_json FROM audit_events WHERE request_id = @Id AND event_type = 'StepCompleted' AND details_json IS NOT NULL ORDER BY id", new { Id = d });
        _k.Check(details.SequenceEqual(new[] { selection.CapturedJson!, tracking.CapturedJson! }), "12: audit details do not carry the captured values");

        // Cancel is allowed at the dispatch step itself, and refused once the parcel is dispatched.
        var atDispatch = await _k.CreateAsync(_k.Requester, "courier", courier);
        await _k.ActAsync(_k.Admin, atDispatch, RequestAction.Complete, null, new() { ["courierCompany"] = ProbeKit.Json("Example Couriers") });
        _k.Check((await _k.RequestAsync(atDispatch)).CurrentStepKey == "dispatch", "7: could not reach the dispatch step");
        await _k.ActAsync(_k.Requester, atDispatch, RequestAction.Cancel, "parcel not needed");
        var rDispatch = await _k.RequestAsync(atDispatch);
        _k.Check(rDispatch.CurrentStatus == "Cancelled" && await _k.ActiveActorCountAsync(atDispatch) == 0, "7: cancel at the courier dispatch step failed");

        var e = await _k.CreateAsync(_k.Requester, "courier", courier);
        await _k.ActAsync(_k.Admin, e, RequestAction.Complete, null, new() { ["courierCompany"] = ProbeKit.Json("Example Couriers") });
        await _k.ActAsync(_k.Admin, e, RequestAction.Complete);
        await _k.ExpectCancelLockedAsync("7: cancel after dispatch (tracking step)", _k.Requester, e, "parcel not needed");
        await _k.ActAsync(_k.Admin, e, RequestAction.Complete, null, new() { ["trackingNumber"] = ProbeKit.Json("TRK999") });
        await _k.ExpectCancelLockedAsync("7: cancel at the courier delivery confirmation", _k.Requester, e, "parcel not needed");
        await _k.ActAsync(_k.Requester, e, RequestAction.Complete);
        var re = await _k.RequestAsync(e);
        _k.Check(re.CurrentStatus == "InProgress" && re.CurrentStepKey == "pod-upload", "7: the locked courier request is not at pod-upload after the delivery confirmation");
        await _k.ExpectCancelLockedAsync("7: cancel at the courier proof of delivery step", _k.Requester, e, "parcel not needed");
        _k.CourierAtProof.Add(e);
        _k.Pass("12 captured values");

        await RunAdminOverrideAsync(courier);
    }

    // The Admin may reject any in-progress request; only the requester may cancel; nobody else may do either.
    private async Task RunAdminOverrideAsync(object courier)
    {
        const string Company = "Example Couriers";
        var stationery = new { item = "Pencil", quantity = 5 };

        async Task<long> NewCourierAsync() => await _k.CreateAsync(_k.Requester, "courier", courier);
        async Task<long> CourierAtDispatchAsync()
        {
            var id = await NewCourierAsync();
            await _k.ActAsync(_k.Admin, id, RequestAction.Complete, null, new() { ["courierCompany"] = ProbeKit.Json(Company) });
            return id;
        }

        // Admin rejects a Courier request at its task step: the reason is required first, then the version.
        var r1 = await NewCourierAsync();
        var versionBefore = (await _k.RequestAsync(r1)).RowVersion;
        await _k.ExpectAsync<ValidationException>("13: admin reject without a reason", () => _k.ActAsync(_k.Admin, r1, RequestAction.Reject, "  "));
        await _k.ExpectAsync<ConflictException>("13: admin reject on a stale version", () => _k.Service.ActAsync(_k.Admin, r1,
            new ActionCommand { Action = RequestAction.Reject, Comment = "stale", ExpectedRowVersion = versionBefore - 1 }, default));
        await _k.ActAsync(_k.Admin, r1, RequestAction.Reject, "  wrong courier  ");
        var rr1 = await _k.RequestAsync(r1);
        var step1 = await _k.StepAsync(r1, "courier-selection");
        _k.Check(rr1.CurrentStatus == "Rejected" && rr1.ApprovalStatus == "Rejected" && rr1.RowVersion == versionBefore + 1
            && rr1.ResponsibleEmployeeId is null && rr1.ResponsibleRole is null, "13: admin reject at a task step did not end the request as Rejected");
        _k.Check(step1.State == "Rejected" && step1.Comment == "wrong courier" && step1.ActedByName == _k.Admin.Name, "13: the rejected step does not carry the reason and the admin");
        _k.Check(await _k.ActiveActorCountAsync(r1) == 0, "13: actors were left active after the reject");
        _k.Check(await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Rejected' AND actor_role = 'Admin' AND comment = 'wrong courier' AND step_key = 'courier-selection'",
            new { Id = r1 }) == 1, "13: the reject audit event is missing the admin role or the reason");

        // A finished request offers nothing to anyone, including an admin.
        var fingerprint = await _k.FingerprintAsync(r1);
        await _k.ExpectNotAllowedAsync("13: reject a rejected request", () => _k.ActAsync(_k.Admin, r1, RequestAction.Reject, "again"));
        await _k.ExpectNotAllowedAsync("13: cancel a rejected request", () => _k.ActAsync(_k.SysAdmin, r1, RequestAction.Cancel, "again"));
        _k.Check(await _k.FingerprintAsync(r1) == fingerprint, "13: a refused action on a rejected request changed it");
        foreach (var viewer in new[] { _k.Admin, _k.SysAdmin, _k.Requester })
        {
            _k.Check(AllowedActionsCalculator.Compute(viewer, RequestStatus.Rejected, _k.Requester.EmployeeId!.Value, null, null,
                Array.Empty<ActorRow>()).Count == 0, "13: a rejected request offers actions");
        }

        // Admin rejects a Stationery request at a task step it does not hold; System admin and Management may not.
        var s1 = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        await _k.AdvanceStationeryAsync(s1, "stock-check");
        _k.Check((await _k.RequestAsync(s1)).CurrentStepKey == "stock-check", "13: could not reach stock-check");
        var s1Print = await _k.FingerprintAsync(s1);
        await _k.ExpectNotAllowedAsync("13: system admin reject at a task step", () => _k.ActAsync(_k.SysAdmin, s1, RequestAction.Reject, "no stock budget"));
        await _k.ExpectNotAllowedAsync("13: management reject at a task step", () => _k.ActAsync(_k.Management, s1, RequestAction.Reject, "no stock budget"));
        _k.Check(await _k.FingerprintAsync(s1) == s1Print, "13: a refused reject at a task step changed the request");
        await _k.ActAsync(_k.Admin, s1, RequestAction.Reject, "no stock budget");
        var rs1 = await _k.RequestAsync(s1);
        _k.Check(rs1.CurrentStatus == "Rejected" && rs1.ApprovalStatus == "Rejected", "13: admin reject at a task step failed");
        _k.Check(await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Rejected' AND actor_role = 'Admin' AND actor_name = @Name",
            new { Id = s1, _k.Admin.Name }) == 1, "13: the reject audit event does not show the Admin role");

        // A real approver still rejects under their own role label.
        var s2 = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        await _k.ActAsync(_k.Manager, s2, RequestAction.Reject, "not needed");
        _k.Check(await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Rejected' AND actor_role = 'Reporting manager'", new { Id = s2 }) == 1,
            "13: the manager reject lost its role label");

        // Admin cannot cancel somebody else's request; the requester can, and the approval status is kept.
        var c1 = await NewCourierAsync();
        var c1Print = await _k.FingerprintAsync(c1);
        await _k.ExpectNotAllowedAsync("13: admin cancel of another person's request", () => _k.ActAsync(_k.Admin, c1, RequestAction.Cancel, "  requester left  "));
        _k.Check(await _k.FingerprintAsync(c1) == c1Print, "13: a refused admin cancel changed the request");
        await _k.ActAsync(_k.Requester, c1, RequestAction.Cancel, "  requester left  ");
        var rc1 = await _k.RequestAsync(c1);
        _k.Check(rc1.CurrentStatus == "Cancelled" && rc1.ApprovalStatus == "Approved" && await _k.ActiveActorCountAsync(c1) == 0, "13: requester cancel failed");
        _k.Check(await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Cancelled' AND actor_role = 'Requester' AND actor_name = @Name AND comment = 'requester left'",
            new { Id = c1, _k.Requester.Name }) == 1, "13: the cancel audit event does not show the requester and the reason");

        var c2 = await _k.CreateAsync(_k.Requester, "stationery", stationery);
        var c2Print = await _k.FingerprintAsync(c2);
        await _k.ExpectAsync<ValidationException>("13: system admin cancel without a reason", () => _k.ActAsync(_k.SysAdmin, c2, RequestAction.Cancel, null));
        await _k.ExpectNotAllowedAsync("13: system admin cancel of another person's request", () => _k.ActAsync(_k.SysAdmin, c2, RequestAction.Cancel, "duplicate"));
        _k.Check(await _k.FingerprintAsync(c2) == c2Print, "13: a refused system admin cancel changed the request");
        _k.Check(await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Cancelled'", new { Id = c2 }) == 0,
            "13: a refused system admin cancel wrote a Cancelled audit event");

        // The cancel lock holds for the requester; the admin cannot cancel at all; reject is never locked.
        var locked = await CourierAtDispatchAsync();
        await _k.ActAsync(_k.Admin, locked, RequestAction.Complete);
        await _k.ExpectNotAllowedAsync("13: admin cancel after dispatch", () => _k.ActAsync(_k.Admin, locked, RequestAction.Cancel, "too late"));
        await _k.ExpectNotAllowedAsync("13: system admin cancel after dispatch", () => _k.ActAsync(_k.SysAdmin, locked, RequestAction.Cancel, "too late"));
        await _k.ExpectCancelLockedAsync("13: requester cancel after dispatch", _k.Requester, locked, "too late");
        _k.Check(AllowedActionsCalculator.Compute(_k.Admin, RequestStatus.InProgress, _k.Requester.EmployeeId!.Value, 3, StepType.Task,
            Array.Empty<ActorRow>(), true).SequenceEqual(new[] { RequestAction.Reject }), "13: a locked request should offer an admin only Reject");
        _k.Check(AllowedActionsCalculator.Compute(_k.SysAdmin, RequestStatus.InProgress, _k.Requester.EmployeeId!.Value, 3, StepType.Task,
            Array.Empty<ActorRow>(), true).Count == 0, "13: a locked request should offer a system admin nothing");
        await _k.ActAsync(_k.Admin, locked, RequestAction.Reject, "parcel lost");
        _k.Check((await _k.RequestAsync(locked)).CurrentStatus == "Rejected", "13: admin reject after the lock failed");

        // Nobody else may reject at a task step or cancel somebody else's request, and nothing is written.
        var target = await NewCourierAsync();
        var untouched = await _k.FingerprintAsync(target);
        var others = new (string Name, ActorContext Actor)[]
        {
            ("employee", _k.Uninvolved), ("manager", _k.Manager), ("finance", _k.Finance), ("hr", _k.Hr),
            ("store", _k.Store), ("security", _k.Security), ("management", _k.Management), ("system admin", _k.SysAdmin),
            ("admin (cancel only)", _k.Admin)
        };
        foreach (var (name, actor) in others)
        {
            if (actor != _k.Admin)
            {
                await _k.ExpectRefusedAsync($"13: {name} reject at a task step", actor, target, () => _k.ActAsync(actor, target, RequestAction.Reject, "no"));
            }
            await _k.ExpectRefusedAsync($"13: {name} cancel of another person's request", actor, target, () => _k.ActAsync(actor, target, RequestAction.Cancel, "no"));
        }
        _k.Check(await _k.FingerprintAsync(target) == untouched, "13: a refused call changed the request");

        // allowedActions by role at a task step the viewer does not hold, then for the requester.
        var requesterId = _k.Requester.EmployeeId!.Value;
        IReadOnlyList<RequestAction> Offered(ActorContext viewer) => AllowedActionsCalculator.Compute(
            viewer, RequestStatus.InProgress, requesterId, 2, StepType.Task, Array.Empty<ActorRow>());
        _k.Check(Offered(_k.Admin).SequenceEqual(new[] { RequestAction.Reject }), "13: an admin is not offered Reject only");
        foreach (var (name, actor) in others.Where(o => o.Actor != _k.Admin))
        {
            _k.Check(Offered(actor).Count == 0, $"13: {name} is offered an action at a task step they do not hold");
        }
        _k.Check(Offered(_k.Requester).SequenceEqual(new[] { RequestAction.Cancel }), "13: the requester is not offered only Cancel");

        // The requester still cancels their own request.
        var own = await NewCourierAsync();
        await _k.ActAsync(_k.Requester, own, RequestAction.Cancel, "not needed");
        _k.Check(await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'Cancelled' AND actor_role = 'Requester'", new { Id = own }) == 1,
            "13: the requester cancel lost its label");

        // An inconsistent request (its current step is not pending) is refused and nothing is written.
        var broken = await NewCourierAsync();
        await _k.ExecuteRawAsync($"UPDATE request_steps SET state = 'Done' WHERE request_id = {broken} AND step_key = 'courier-selection'");
        var brokenPrint = await _k.FingerprintAsync(broken);
        await _k.ExpectNotAllowedAsync("13: admin reject when the current step is not pending", () => _k.ActAsync(_k.Admin, broken, RequestAction.Reject, "no"));
        _k.Check(await _k.FingerprintAsync(broken) == brokenPrint, "13: the refused reject on an inconsistent request wrote something");
        _k.Pass("13 admin override reject; cancel for the requester only");
    }

    private async Task RunFieldTypesAsync()
    {
        if (await _k.Definitions.GetActiveAsync("fieldcheck") is null)
        {
            _k.Logger.LogInformation("Engine probe: fieldcheck is not loaded, field type check skipped");
            return;
        }

        async Task<int> FirstIdAsync(string table) =>
            await _k.ScalarAsync<int>($"SELECT MIN(id) FROM {table}");

        var employee = await FirstIdAsync("employees");
        var department = await FirstIdAsync("departments");
        var project = await FirstIdAsync("projects");
        var location = await FirstIdAsync("locations");
        var costCentre = await FirstIdAsync("cost_centres");

        Dictionary<string, object?> Valid() => new()
        {
            ["title"] = "All types",
            ["notes"] = "Some longer notes",
            ["quantity"] = 2,
            ["amount"] = 12.5m,
            ["needBy"] = "2026-12-20",
            ["startsAt"] = "2026-12-20T10:30:00+05:30",
            ["urgent"] = true,
            ["kind"] = "a",
            ["tags"] = new[] { "x", "y" },
            ["assignee"] = employee,
            ["ownerDepartment"] = department,
            ["relatedProject"] = project,
            ["site"] = location,
            ["chargeTo"] = costCentre
        };

        var id = await _k.CreateAsync(_k.Requester, "fieldcheck", Valid());
        var created = await _k.RequestAsync(id);
        _k.Check(created.CurrentStepKey == "review" && created.RequestNo.StartsWith("FLD-"), "15: a fully valid payload was not routed to review");
        var amount = await _k.ScalarAsync<long>("SELECT json_extract(payload_json, '$.amount') FROM requests WHERE id = @Id", new { Id = id });
        _k.Check(amount == 1250, "15: money was not stored as paise");
        await _k.ActAsync(_k.Requester, id, RequestAction.Cancel, "field check done");

        var variants = new (string Field, Action<Dictionary<string, object?>> Change)[]
        {
            ("title", p => p.Remove("title")),
            ("quantity", p => p["quantity"] = 0),
            ("quantity", p => p["quantity"] = 11),
            ("amount", p => p["amount"] = 10.123m),
            ("needBy", p => p["needBy"] = "2026-02-30"),
            ("startsAt", p => p["startsAt"] = "not a date"),
            ("urgent", p => p["urgent"] = "yes"),
            ("kind", p => p["kind"] = "q"),
            ("tags", p => p["tags"] = new[] { "x", "q" }),
            ("assignee", p => p["assignee"] = 999999),
            ("ownerDepartment", p => p["ownerDepartment"] = 999999),
            ("relatedProject", p => p["relatedProject"] = 999999),
            ("site", p => p["site"] = 999999),
            ("chargeTo", p => p["chargeTo"] = 999999),
            ("bogus", p => p["bogus"] = "x")
        };
        foreach (var (field, change) in variants)
        {
            var payload = Valid();
            change(payload);
            await _k.ExpectFieldAsync($"15: invalid {field}", field, () => _k.CreateAsync(_k.Requester, "fieldcheck", payload));
        }
        _k.Pass("15 every field type");
    }

    private async Task RunOwnershipAndVisibilityAsync(long closed, long rejected)
    {
        var stationery = await _k.Definitions.GetActiveAsync("stationery") ?? throw _k.Fail("16: stationery is not active");
        var courier = await _k.Definitions.GetActiveAsync("courier") ?? throw _k.Fail("16: courier is not active");

        await _k.ExpectFieldAsync("16: definition of another module", "definitionId",
            () => _k.CreateAsync(_k.Requester, "stationery", new { item = "Pen", quantity = 1 }, null, checked((int)courier.Id)));
        await _k.ExpectFieldAsync("16: unknown definition", "definitionId",
            () => _k.CreateAsync(_k.Requester, "stationery", new { item = "Pen", quantity = 1 }, null, 99999999));
        var earlier = await _k.QueryAsync<int>(
            "SELECT id FROM module_definitions WHERE code = 'stationery' AND version < (SELECT MAX(version) FROM module_definitions WHERE code = 'stationery')");
        if (earlier.Count > 0)
        {
            await _k.ExpectFieldAsync("16: superseded definition version", "definitionId",
                () => _k.CreateAsync(_k.Requester, "stationery", new { item = "Pen", quantity = 1 }, null, earlier[0]));
        }
        var accepted = await _k.CreateAsync(_k.Requester, "stationery", new { item = "Pen", quantity = 1 }, null, checked((int)stationery.Id));
        await _k.ActAsync(_k.Requester, accepted, RequestAction.Cancel, "ownership check done");
        _k.Pass("16 definition ownership");

        var ex = await _k.ExpectAsync<ForbiddenException>("17: create without an employee profile",
            () => _k.CreateAsync(_k.NoEmployee, "stationery", new { item = "Pen", quantity = 1 }));
        _k.Check(ex.Code == ErrorCodes.NO_EMPLOYEE_PROFILE, $"17: code was {ex.Code}");
        _k.Pass("17 no employee profile");

        var lonely = new ActorContext("probe-lonely", "Nobody", null, new HashSet<string> { Roles.Employee });
        _k.Check(await _k.Access.CanViewAsync(_k.Requester, closed, default), "18: the requester cannot view");
        _k.Check(await _k.Access.CanViewAsync(_k.Manager, closed, default), "18: a past step actor cannot view");
        _k.Check(await _k.Access.CanViewAsync(_k.Store, closed, default), "18: a role actor who acted cannot view");
        _k.Check(await _k.Access.CanViewAsync(_k.Admin, closed, default), "18: an admin cannot view");
        _k.Check(await _k.Access.CanViewAsync(_k.NoEmployee, closed, default), "18: the system admin cannot view");
        _k.Check(await _k.Access.CanViewAsync(_k.Management, closed, default), "18: management cannot view");
        _k.Check(!await _k.Access.CanViewAsync(_k.Uninvolved, closed, default), "18: an uninvolved employee can view");
        _k.Check(!await _k.Access.CanViewAsync(lonely, closed, default), "18: an employee-role actor with no employee id can view");
        _k.Check(await _k.Access.CanViewAsync(_k.Manager, rejected, default), "18: the manager cannot view a rejected request");
        _k.Pass("18 visibility");
    }
}
