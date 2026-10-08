using System.Text.Json;
using AdminDesk.Application.Engine;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Money;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Requests;

internal sealed class ProbeActorRow
{
    public string? RoleName { get; set; }
    public long? EmployeeId { get; set; }
}

internal sealed class ProbeMoneyRow
{
    public long Amount { get; set; }
    public string Type { get; set; } = string.Empty;
}

internal sealed class ProbeCostRow
{
    public long Cost { get; set; }
    public string Type { get; set; } = string.Empty;
}

// Checks 20 to 25 of the engine probe and its pinning mode. They use the routing sample
// definition, which only exists when the probe run supplies it through the override folder.
internal sealed class EngineProbeRoutingChecks
{
    private readonly ProbeKit _k;

    public EngineProbeRoutingChecks(ProbeKit kit)
    {
        _k = kit;
    }

    private static Dictionary<string, JsonElement> Damaged(bool value) => new() { ["damaged"] = ProbeKit.Json(value) };

    private Task<long> CreateRouteAsync(string title, decimal amount, string? remarks = null) =>
        _k.CreateAsync(_k.Requester, "routingcheck", new { title, amount }, new CommonFields { Remarks = remarks });

    public async Task RunAsync(CancellationToken ct)
    {
        if (await _k.Definitions.GetActiveAsync("routingcheck") is null)
        {
            _k.Logger.LogInformation("Engine probe: routingcheck is not loaded, routing checks skipped");
        }
        else
        {
            await LateRoutingAsync();
            await ActivationColumnsAsync();
            await MoneyAsync();
        }

        await HookSeamAsync();
        await UnknownRequestAsync(ct);

        if (await _k.Definitions.GetActiveAsync("routingcheck") is not null)
        {
            await PinnedInFlightAsync();
        }

        var stnCreated = _k.CreatedByPrefix.GetValueOrDefault("STN");
        var stnLast = await _k.CounterAsync("STN");
        var stnRows = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM requests WHERE request_no LIKE 'STN-%'");
        _k.Check(stnLast >= stnCreated && stnLast <= stnRows, $"14: STN counter {stnLast} does not fit {stnRows} stored requests");
    }

    private async Task ExpectStatesAsync(long id, string what, params (string Key, string State)[] expected)
    {
        var steps = await _k.StepsAsync(id);
        foreach (var (key, state) in expected)
        {
            var actual = steps.Single(s => s.StepKey == key).State;
            _k.Check(actual == state, $"{what}: step {key} is {actual}, expected {state}");
        }
    }

    // 20: conditional steps are decided only when they become the next to activate.
    private async Task LateRoutingAsync()
    {
        // Request A: low amount, nothing damaged.
        var a = await CreateRouteAsync("Routing A", 20000m);
        var steps = await _k.StepsAsync(a);
        var review = steps.Single(s => s.StepKey == "review");
        _k.Check(review.State == "Pending" && review.ActivatedUtc is not null, "20: review is not the active first step");
        _k.Check(steps.Where(s => s.StepKey != "review").All(s => s.ActivatedUtc is null), "20: a step other than review was activated at creation");
        await ExpectStatesAsync(a, "20 creation", ("damage-assessment", "Upcoming"), ("high-value-approval", "Upcoming"));
        _k.Check((await _k.RequestAsync(a)).ApprovalStatus == "Pending", "20: approval is not Pending while review is open");

        await _k.ActAsync(_k.Admin, a, RequestAction.Approve);
        var ra = await _k.RequestAsync(a);
        _k.Check(ra.ApprovalStatus == "Approved" && ra.CurrentStepKey == "inspection", "20: after review the approval is not Approved at inspection");
        await ExpectStatesAsync(a, "20 after review", ("damage-assessment", "Upcoming"), ("high-value-approval", "Upcoming"));

        await _k.ActAsync(_k.Security, a, RequestAction.Complete, null, Damaged(false));
        ra = await _k.RequestAsync(a);
        await ExpectStatesAsync(a, "20 A after inspection", ("damage-assessment", "NotRequired"), ("high-value-approval", "NotRequired"), ("extra-check", "Pending"));
        _k.Check(ra.ApprovalStatus == "Approved" && ra.CurrentStepKey == "extra-check", "20: A is not at extra-check/Approved");

        // Request B: high amount and damaged.
        var b = await CreateRouteAsync("Routing B", 80000m);
        await _k.ActAsync(_k.Admin, b, RequestAction.Approve);
        await _k.ActAsync(_k.Security, b, RequestAction.Complete, null, Damaged(true));
        var rb = await _k.RequestAsync(b);
        var damage = await _k.StepAsync(b, "damage-assessment");
        _k.Check(damage.State == "Pending" && damage.ActivatedUtc is not null && rb.ResponsibleRole == "Finance" && rb.CurrentStepKey == "damage-assessment",
            "20: damage assessment is not waiting for Finance");
        await ExpectStatesAsync(b, "20 B damaged", ("high-value-approval", "Upcoming"));
        _k.Check(rb.ApprovalStatus == "Pending", "20: approval did not move back to Pending");

        await _k.ActAsync(_k.Finance, b, RequestAction.Approve);
        rb = await _k.RequestAsync(b);
        await ExpectStatesAsync(b, "20 B finance", ("high-value-approval", "Pending"));
        _k.Check(rb.ResponsibleRole == "Management" && rb.ApprovalStatus == "Pending" && rb.CurrentStepKey == "high-value-approval",
            "20: high value approval is not waiting for Management");

        await _k.ActAsync(_k.Management, b, RequestAction.Approve);
        rb = await _k.RequestAsync(b);
        _k.Check(rb.ApprovalStatus == "Approved" && rb.CurrentStepKey == "extra-check", "20: B is not at extra-check/Approved");
        var actorRows = await _k.QueryAsync<ProbeActorRow>(
            "SELECT role_name AS RoleName, employee_id AS EmployeeId FROM request_step_actors WHERE request_id = @Id AND is_active = 1", new { Id = b });
        _k.Check(actorRows.Count == 1 && actorRows[0].RoleName == Roles.Security && actorRows[0].EmployeeId is null,
            "20: extra-check does not wait on the Security role");

        // Complete the remaining steps; A also records a repair cost (checked in 22).
        await _k.ActAsync(_k.Security, a, RequestAction.Complete, null, new() { ["repairCost"] = ProbeKit.Json(1250.75m) });
        await _k.ActAsync(_k.Requester, a, RequestAction.Complete);
        await _k.ActAsync(_k.Security, b, RequestAction.Complete);
        await _k.ActAsync(_k.Requester, b, RequestAction.Complete);
        _k.Check((await _k.RequestAsync(a)).CurrentStatus == "Closed" && (await _k.RequestAsync(b)).CurrentStatus == "Closed", "20: the routing requests did not close");

        // A required captured value that is missing writes nothing.
        var c = await CreateRouteAsync("Routing C", 80000.50m);
        await _k.ActAsync(_k.Admin, c, RequestAction.Approve);
        var before = await _k.FingerprintAsync(c);
        await _k.ExpectFieldAsync("20: inspection without damaged", "damaged", () => _k.ActAsync(_k.Security, c, RequestAction.Complete));
        _k.Check(before == await _k.FingerprintAsync(c), "20: a refused inspection changed the request");
        _k.Pass("20 late conditional routing");
        _routeA = a;
        _routeB = b;
        _routeC = c;
    }

    private long _routeA;
    private long _routeB;
    private long _routeC;

    // 21: activation and unused columns.
    private async Task ActivationColumnsAsync()
    {
        var ids = _k.Created.ToArray();
        var activeUnset = await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM requests r JOIN request_steps s ON s.request_id = r.id AND s.seq = r.current_step_seq " +
            "WHERE r.id IN @Ids AND r.current_status = 'InProgress' AND s.activated_utc IS NULL", new { Ids = ids });
        _k.Check(activeUnset == 0, "21: an active step has no activation time");
        var early = await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM requests r JOIN request_steps s ON s.request_id = r.id " +
            "WHERE r.id IN @Ids AND r.current_status = 'InProgress' AND s.seq > r.current_step_seq AND s.activated_utc IS NOT NULL", new { Ids = ids });
        _k.Check(early == 0, "21: a step not reached yet has an activation time");
        var due = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM request_steps WHERE due_utc IS NOT NULL");
        _k.Check(due == 0, "21: a step has a due time");
        var unused = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM requests WHERE amount_minor IS NOT NULL OR parent_request_id IS NOT NULL");
        _k.Check(unused == 0, "21: amount_minor or parent_request_id is set");
        _k.Pass("21 activation columns");
    }

    // 22: money in minor units and the exact limit boundary.
    private async Task MoneyAsync()
    {
        var stored = await _k.QueryAsync<ProbeMoneyRow>(
            "SELECT json_extract(payload_json, '$.amount') AS Amount, typeof(json_extract(payload_json, '$.amount')) AS Type FROM requests WHERE id = @Id",
            new { Id = _routeC });
        _k.Check(stored.Count == 1 && stored[0].Amount == 8000050 && stored[0].Type == "integer", "22: 80000.50 is not stored as the integer 8000050");
        _k.Check(MoneyConverter.ToRupees(stored[0].Amount) == 80000.50m, "22: paise do not convert back to rupees");

        var captured = await _k.QueryAsync<ProbeCostRow>(
            "SELECT json_extract(captured_json, '$.repairCost') AS Cost, typeof(json_extract(captured_json, '$.repairCost')) AS Type " +
            "FROM request_steps WHERE step_key = 'extra-check' AND request_id = @Id", new { Id = _routeA });
        _k.Check(captured.Count == 1 && captured[0].Cost == 125075 && captured[0].Type == "integer", "22: repairCost is not stored as integer paise");
        _k.Check(MoneyConverter.ToRupees(captured[0].Cost) == 1250.75m, "22: repairCost does not convert back to rupees");
        _k.Check((await _k.StepAsync(_routeB, "extra-check")).CapturedJson is null, "22: a skipped optional capture stored a value");

        var exact = await CreateRouteAsync("Boundary exact", 50000.00m);
        await _k.ActAsync(_k.Admin, exact, RequestAction.Approve);
        await _k.ActAsync(_k.Security, exact, RequestAction.Complete, null, Damaged(false));
        await ExpectStatesAsync(exact, "22 exact limit", ("high-value-approval", "NotRequired"));

        var above = await CreateRouteAsync("Boundary above", 50000.01m);
        await _k.ActAsync(_k.Admin, above, RequestAction.Approve);
        await _k.ActAsync(_k.Security, above, RequestAction.Complete, null, Damaged(false));
        await ExpectStatesAsync(above, "22 above limit", ("high-value-approval", "Pending"));
        _k.Pass("22 money in minor units");
    }

    // 23: the add-on seam runs inside the action transaction and rolls it back on failure.
    private async Task HookSeamAsync()
    {
        var stationery = new { item = "Marker", quantity = 3 };
        var hookSwitch = _k.Switch;
        try
        {
            hookSwitch.Events.Clear();
            hookSwitch.Mode = ProbeHookMode.Record;
            var m = await _k.CreateAsync(_k.Requester, "stationery", stationery);
            await _k.ActAsync(_k.Manager, m, RequestAction.Approve);
            await _k.ActAsync(_k.Store, m, RequestAction.Reject, "stop");
            hookSwitch.Mode = ProbeHookMode.Off;

            var events = hookSwitch.Events.ToList();
            var creating = events.SingleOrDefault(e => e.Name == "creating");
            var done = events.SingleOrDefault(e => e.Name == "step-done");
            var terminal = events.SingleOrDefault(e => e.Name == "terminal");
            _k.Check(creating is not null && done is not null && terminal is not null, "23: not every hook ran");
            _k.Check(creating!.HasTransaction && creating.SeenInOwnTransaction == 1 && creating.SeenElsewhere == 0,
                "23: the creating hook did not run inside the open transaction");
            _k.Check(done!.HasTransaction && terminal!.HasTransaction, "23: a hook ran without a transaction");
            _k.Check(terminal!.FinalStatus == RequestStatus.Rejected, "23: the terminal hook got the wrong final status");

            // A failing step hook rolls the whole action back.
            var n = await _k.CreateAsync(_k.Requester, "stationery", stationery);
            var before = await _k.FingerprintAsync(n);
            hookSwitch.Mode = ProbeHookMode.ThrowOnStepDone;
            var failure = await _k.ExpectAsync<InvalidOperationException>("23: failing step hook", () => _k.ActAsync(_k.Manager, n, RequestAction.Approve));
            hookSwitch.Mode = ProbeHookMode.Off;
            _k.Check(failure.Message == ProbeSwitch.FailureMessage, "23: the hook exception was replaced");
            _k.Check(before == await _k.FingerprintAsync(n), "23: a failing step hook left changes behind");

            // A failing creating hook refuses the create and gives the number back.
            var counter = await _k.CounterAsync("STN");
            var rows = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM requests");
            hookSwitch.Mode = ProbeHookMode.ThrowOnCreating;
            await _k.ExpectAsync<InvalidOperationException>("23: failing creating hook", () => _k.CreateAsync(_k.Requester, "stationery", stationery));
            hookSwitch.Mode = ProbeHookMode.Off;
            _k.Check(counter == await _k.CounterAsync("STN") && rows == await _k.ScalarAsync<long>("SELECT COUNT(*) FROM requests"),
                "23: a failing creating hook left a request or a used number behind");
            await _k.ActAsync(_k.Requester, n, RequestAction.Cancel, "hook check done");
        }
        finally
        {
            hookSwitch.Mode = ProbeHookMode.Off;
        }
        _k.Pass("23 add-on seam");
    }

    // 24: an unknown request id is NOT_FOUND before anything else.
    private async Task UnknownRequestAsync(CancellationToken ct)
    {
        var approve = new ActionCommand { Action = RequestAction.Approve, ExpectedRowVersion = 1 };
        var cancel = new ActionCommand { Action = RequestAction.Cancel, Comment = "gone", ExpectedRowVersion = 1 };
        var first = await _k.ExpectAsync<NotFoundException>("24: approve unknown", () => _k.Service.ActAsync(_k.Admin, 999999999, approve, ct));
        var second = await _k.ExpectAsync<NotFoundException>("24: cancel unknown", () => _k.Service.ActAsync(_k.Requester, 999999999, cancel, ct));
        _k.Check(first.Code == ErrorCodes.NOT_FOUND && second.Code == ErrorCodes.NOT_FOUND, "24: wrong error code for an unknown request");
        _k.Pass("24 unknown request id");
    }

    // 25: stage one of the pinning check. A request is left in flight on version 1.
    private async Task PinnedInFlightAsync()
    {
        var id = await CreateRouteAsync("Pin probe", 20000m, "pin-probe");
        await _k.ActAsync(_k.Admin, id, RequestAction.Approve);
        var request = await _k.RequestAsync(id);
        var steps = await _k.StepsAsync(id);
        _k.Check(request.DefinitionVersion == 1 && steps.Count == 6 && request.CurrentStepKey == "inspection", "25: the in-flight request is not pinned to version 1 with six steps");
        _k.Pass("25 pinning in flight");
    }

    // Second run: a newer version is active, the request from stage one stays on version 1.
    public async Task RunPinningAsync(CancellationToken ct)
    {
        var versions = await _k.QueryAsync<int>("SELECT version FROM module_definitions WHERE code = 'routingcheck' ORDER BY version");
        _k.Check(versions.SequenceEqual(new[] { 1, 2 }), $"pinning: stored versions are {string.Join(",", versions)}");
        var active = await _k.Definitions.GetActiveAsync("routingcheck");
        _k.Check(active?.Definition.Version == 2, "pinning: version 2 is not the active one");

        var ids = await _k.QueryAsync<long>("SELECT id FROM requests WHERE remarks = 'pin-probe'");
        _k.Check(ids.Count == 1, $"pinning: expected one in-flight request, found {ids.Count}");
        var id = ids[0];
        var request = await _k.RequestAsync(id);
        _k.Check(request.DefinitionVersion == 1 && request.CurrentStatus == "InProgress", "pinning: the in-flight request left version 1");
        var steps = await _k.StepsAsync(id);
        _k.Check(steps.Count == 6 && steps.Any(s => s.StepKey == "sign-off"), "pinning: the in-flight request lost a step");

        await _k.ActAsync(_k.Security, id, RequestAction.Complete, null, Damaged(false));
        await _k.ActAsync(_k.Security, id, RequestAction.Complete);
        _k.Check((await _k.RequestAsync(id)).CurrentStepKey == "sign-off", "pinning: the request did not follow its own steps to sign-off");
        await _k.ActAsync(_k.Requester, id, RequestAction.Complete);
        _k.Check((await _k.RequestAsync(id)).CurrentStatus == "Closed" && (await _k.StepsAsync(id)).Count == 6, "pinning: the in-flight request did not close on version 1");

        var fresh = await CreateRouteAsync("Pin fresh", 20000m);
        var freshRequest = await _k.RequestAsync(fresh);
        var freshSteps = await _k.StepsAsync(fresh);
        _k.Check(freshRequest.DefinitionVersion == 2 && freshSteps.Count == 5 && freshSteps.All(s => s.StepKey != "sign-off"),
            "pinning: a new request is not on version 2 with five steps");
    }
}
