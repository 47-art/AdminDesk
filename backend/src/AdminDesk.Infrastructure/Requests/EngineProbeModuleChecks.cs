using System.Text.Json;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.Application.Requests;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Requests;

internal sealed class ProbeMasterRow
{
    public string Status { get; set; } = string.Empty;
    public long? HolderEmployeeId { get; set; }
    public string? ActivationDate { get; set; }
    public string? ItemCondition { get; set; }
}

internal sealed class ProbeHistoryRow
{
    public string EventType { get; set; } = string.Empty;
    public long? EmployeeId { get; set; }
    public long? RequestId { get; set; }
    public string? ItemCondition { get; set; }
    public long? CostMinor { get; set; }
    public string? Notes { get; set; }
}

// Checks 39 to 45: the allocation and service modules walked end to end with the right people, the exact
// step order of every module, the show-when rule, the optional Finance step and the effect on the masters.
// These checks change the real masters of the throw-away data folder, so they run last.
internal sealed class EngineProbeModuleChecks
{
    private static readonly Dictionary<string, string[]> ExpectedSteps = new()
    {
        ["sim"] = new[] { "manager-approval", "admin-verification", "finance-approval", "sim-availability", "sim-allocation", "employee-acknowledgement", "telecom-activation", "sim-master-update" },
        ["sim-return"] = new[] { "sim-return", "admin-verification", "telecom-deactivation", "sim-master-update" },
        ["laptop"] = new[] { "manager-approval", "it-admin-verification", "finance-approval", "asset-availability", "asset-allocation", "employee-acknowledgement", "asset-master-update" },
        ["asset-return"] = new[] { "physical-verification", "condition-check", "damage-loss-calculation", "clearance", "asset-master-update" },
        ["id-card"] = new[] { "hr-verification", "admin-printing", "employee-handover", "acknowledgement", "id-card-master-update" },
        ["welfare"] = new[] { "admin-review", "approval", "action", "expense-entry", "employee-confirmation" },
        ["housekeeping"] = new[] { "admin-assignment", "housekeeping-action", "completion", "employee-confirmation" },
        ["courier"] = new[] { "courier-selection", "dispatch", "tracking-number", "delivery-confirmation", "pod-upload" }
    };

    private readonly ProbeKit _k;
    private readonly IMasterAssetService _masters;
    private long _demo;

    public EngineProbeModuleChecks(ProbeKit kit)
    {
        _k = kit;
        _masters = kit.Services.GetRequiredService<IMasterAssetService>();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _demo = await _k.EmployeeIdAsync("E0010");

        await SimAsync(ct);
        await SimReturnAsync(ct);
        await LaptopAndAssetReturnAsync(ct);
        await IdCardAsync(ct);
        await WelfareAndHousekeepingAsync(ct);
        await CourierAsync();
    }

    // ------------------------------------------------------------------ helpers

    private static JsonElement J(object? value) => ProbeKit.Json(value);

    private async Task AssertStepsAsync(long id, string module)
    {
        var keys = (await _k.StepsAsync(id)).OrderBy(s => s.Seq).Select(s => s.StepKey).ToArray();
        _k.Check(keys.SequenceEqual(ExpectedSteps[module]),
            $"{module}: steps are [{string.Join(", ", keys)}] but expected [{string.Join(", ", ExpectedSteps[module])}]");
    }

    private async Task AtAsync(long id, string stepKey, string what)
    {
        var request = await _k.RequestAsync(id);
        _k.Check(request.CurrentStepKey == stepKey && request.CurrentStatus == "InProgress",
            $"{what}: expected the request at '{stepKey}' but it is at '{request.CurrentStepKey}' ({request.CurrentStatus})");
    }

    private async Task StepStateAsync(long id, string stepKey, string state, string what)
    {
        var step = await _k.StepAsync(id, stepKey);
        _k.Check(step.State == state, $"{what}: step '{stepKey}' is {step.State}, expected {state}");
    }

    // Acts on the named step and checks it was the current one.
    private async Task DoAsync(ActorContext actor, long id, string stepKey, RequestAction action, Dictionary<string, JsonElement>? captured = null)
    {
        await AtAsync(id, stepKey, $"before {stepKey}");
        await _k.ActAsync(actor, id, action, null, captured);
    }

    private async Task ClosedAsync(long id, string what)
    {
        var request = await _k.RequestAsync(id);
        _k.Check(request.CurrentStatus == "Closed" && request.ClosedUtc is not null, $"{what}: the request is {request.CurrentStatus}, expected Closed");
        // Every closed request must also read back as the page does after the last step.
        try
        {
            using var scope = _k.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IRequestQueryService>().GetDetailAsync(_k.Admin, id, default);
        }
        catch (Exception exception)
        {
            _k.Check(false, $"{what}: reading the closed request failed: {exception.Message}");
        }
    }

    private Task<ProbeMasterRow> SimRowAsync(long id) =>
        _k.QueryAsync<ProbeMasterRow>(
            "SELECT status, holder_employee_id AS HolderEmployeeId, activation_date AS ActivationDate, NULL AS ItemCondition FROM sims WHERE id = @Id",
            new { Id = id }).ContinueWith(t => t.Result.Single());

    private Task<ProbeMasterRow> AssetRowAsync(long id) =>
        _k.QueryAsync<ProbeMasterRow>(
            "SELECT status, holder_employee_id AS HolderEmployeeId, NULL AS ActivationDate, item_condition AS ItemCondition FROM assets WHERE id = @Id",
            new { Id = id }).ContinueWith(t => t.Result.Single());

    private async Task<ProbeHistoryRow> LastHistoryAsync(string type, long id) =>
        (await _k.QueryAsync<ProbeHistoryRow>(
            "SELECT event_type AS EventType, employee_id AS EmployeeId, request_id AS RequestId, item_condition AS ItemCondition, " +
            "cost_minor AS CostMinor, notes AS Notes FROM master_history WHERE master_type = @Type AND master_id = @Id ORDER BY id DESC LIMIT 1",
            new { Type = type, Id = id })).Single();

    private async Task<long> FirstAvailableAsync(string kind)
    {
        var items = await _k.Lookups.Find(kind)!.SearchAsync(string.Empty, 50, default);
        _k.Check(items.Count > 0, $"39: the {kind} lookup offers nothing");
        return items[0].Id;
    }

    private async Task<string?> StoredPayloadValueAsync(long requestId, string key) =>
        await _k.ScalarOrNullAsync<string?>("SELECT json_extract(payload_json, '$.' || @Key) FROM requests WHERE id = @Id", new { Key = key, Id = requestId });

    // ------------------------------------------------------------------ 39 SIM

    private async Task SimAsync(CancellationToken ct)
    {
        // Finance is needed only when a cost above the limit (zero by default) is entered.
        var withCost = await _k.CreateAsync(_k.Requester, "sim", new { requestType = "New SIM", reason = "Field work", estimatedCost = 750.5m });
        await AssertStepsAsync(withCost, "sim");
        var shown = (await _k.Definitions.GetActiveAsync("sim"))!.Definition.Limits.Single();
        _k.Check(shown.StepKey == "finance-approval" && shown.LimitKey == "finance-limit" && shown.ValueMinor == 0 && shown.Unit == "INR",
            "39: the SIM module does not ship the Finance limit of zero");

        await DoAsync(_k.Manager, withCost, "manager-approval", RequestAction.Approve);
        await _k.ExpectRefusedAsync("39: the requester approving the Admin verification", _k.Requester, withCost, () => _k.ActAsync(_k.Requester, withCost, RequestAction.Approve));
        await DoAsync(_k.Admin, withCost, "admin-verification", RequestAction.Approve);
        await AtAsync(withCost, "finance-approval", "39: a SIM with a cost");
        await StepStateAsync(withCost, "finance-approval", "Pending", "39: a SIM with a cost");
        await _k.ExpectRefusedAsync("39: Admin approving the Finance step", _k.Admin, withCost, () => _k.ActAsync(_k.Admin, withCost, RequestAction.Approve));
        await DoAsync(_k.Finance, withCost, "finance-approval", RequestAction.Approve);

        // No cost entered, and a cost of exactly the limit, never reach Finance.
        foreach (var (label, payload) in new (string, object)[]
                 {
                     ("a SIM with no cost", new { requestType = "New SIM", reason = "No cost" }),
                     ("a SIM with a cost of zero", new { requestType = "Replacement", reason = "Zero cost", estimatedCost = 0 })
                 })
        {
            var free = await _k.CreateAsync(_k.Requester, "sim", payload);
            await DoAsync(_k.Manager, free, "manager-approval", RequestAction.Approve);
            await DoAsync(_k.Admin, free, "admin-verification", RequestAction.Approve);
            await StepStateAsync(free, "finance-approval", "NotRequired", $"39: {label}");
            await AtAsync(free, "sim-availability", $"39: {label}");
            await _k.ActAsync(_k.Requester, free, RequestAction.Cancel, "Probe check finished");
        }

        // Availability, then allocation with the lookup checked at the capturing step.
        await _k.ExpectRefusedAsync("39: the requester confirming availability", _k.Requester, withCost, () => _k.ActAsync(_k.Requester, withCost, RequestAction.Complete));
        await DoAsync(_k.Admin, withCost, "sim-availability", RequestAction.Complete);
        await AtAsync(withCost, "sim-allocation", "39: allocation");

        var taken = await _k.ScalarAsync<long>("SELECT id FROM sims WHERE status = 'Allocated' AND holder_employee_id IS NOT NULL ORDER BY id LIMIT 1");
        var offered = await FirstAvailableAsync(MasterLookupKinds.AvailableSim);
        await _k.ExpectRefusedAsync("39: the requester allocating a SIM", _k.Requester, withCost, () => _k.ActAsync(_k.Requester, withCost, RequestAction.Complete, null, new() { ["sim"] = J(offered) }));
        await _k.ExpectRefusedAsync("39: Finance allocating a SIM", _k.Finance, withCost, () => _k.ActAsync(_k.Finance, withCost, RequestAction.Complete, null, new() { ["sim"] = J(offered) }));
        await _k.ExpectFieldAsync("39: allocating a SIM that is already held", "sim", () => _k.ActAsync(_k.Admin, withCost, RequestAction.Complete, null, new() { ["sim"] = J(taken) }));
        await _k.ExpectFieldAsync("39: allocating without choosing a SIM", "sim", () => _k.ActAsync(_k.Admin, withCost, RequestAction.Complete));
        await _k.ExpectFieldAsync("39: allocating an unknown SIM", "sim", () => _k.ActAsync(_k.Admin, withCost, RequestAction.Complete, null, new() { ["sim"] = J(987654321) }));
        await AtAsync(withCost, "sim-allocation", "39: refused allocations");

        var sim = await FirstAvailableAsync(MasterLookupKinds.AvailableSim);
        await DoAsync(_k.Admin, withCost, "sim-allocation", RequestAction.Complete, new() { ["sim"] = J(sim) });
        await _k.ExpectRefusedAsync("39: Admin acknowledging for the employee", _k.Admin, withCost, () => _k.ActAsync(_k.Admin, withCost, RequestAction.Complete));
        await DoAsync(_k.Requester, withCost, "employee-acknowledgement", RequestAction.Complete);
        await _k.ExpectFieldAsync("39: activation without a date", "activationDate", () => _k.ActAsync(_k.Admin, withCost, RequestAction.Complete));
        await _k.ExpectFieldAsync("39: activation with a bad date", "activationDate", () => _k.ActAsync(_k.Admin, withCost, RequestAction.Complete, null, new() { ["activationDate"] = J("03/02/2026") }));
        await DoAsync(_k.Admin, withCost, "telecom-activation", RequestAction.Complete, new() { ["activationDate"] = J("2026-02-03") });
        var before = await SimRowAsync(sim);
        _k.Check(before.Status == SimStatuses.Available, "39: the SIM changed before the master update step");
        await DoAsync(_k.Admin, withCost, "sim-master-update", RequestAction.Complete);
        await ClosedAsync(withCost, "39: the SIM request");

        var row = await SimRowAsync(sim);
        _k.Check(row.Status == SimStatuses.Allocated && row.HolderEmployeeId == _demo && row.ActivationDate == "2026-02-03",
            $"39: the SIM master shows {row.Status}/{row.HolderEmployeeId}/{row.ActivationDate}");
        var history = await LastHistoryAsync(MasterTypes.Sim, sim);
        _k.Check(history.EventType == "New SIM" && history.RequestId == withCost && history.EmployeeId == _demo,
            $"39: the SIM history row is {history.EventType} for request {history.RequestId}");
        _k.Check(!await _k.Lookups.ExistsAsync(MasterLookupKinds.AvailableSim, sim, ct), "39: the allocated SIM is still offered as available");
        _k.Check(await _k.Lookups.ExistsAsync(MasterLookupKinds.HeldSim, sim, ct), "39: the allocated SIM is not offered as held");
        _k.Check(await _k.AuditCountAsync(withCost, "MasterUpdated") == 1, "39: the SIM master update is not in the audit trail");

        // The page reads the request back after the step: the label of the SIM that is no longer available
        // must still resolve, and reading must not fail.
        try
        {
            using var scope = _k.Services.CreateScope();
            var detail = await scope.ServiceProvider.GetRequiredService<IRequestQueryService>().GetDetailAsync(_k.Admin, withCost, ct);
            _k.Check(detail.LookupLabels.Values.Any(v => v.Contains(" - ")),
                "39: the allocated SIM label is missing on the closed request");
        }
        catch (Exception exception)
        {
            _k.Check(false, "39: reading the closed SIM request failed: " + exception.Message);
        }
        _simAllocated = sim;
        _k.Pass("39 SIM request: optional Finance step, allocation, activation and master update");
    }

    private long _simAllocated;

    // ------------------------------------------------------------ 40 SIM return

    private async Task SimReturnAsync(CancellationToken ct)
    {
        var other = await _k.ScalarAsync<long>("SELECT id FROM sims WHERE status = 'Allocated' AND holder_employee_id <> @Demo ORDER BY id LIMIT 1", new { Demo = _demo });
        await _k.ExpectFieldAsync("40: returning a SIM held by someone else", "sim", () => _k.CreateAsync(_k.Requester, "sim-return", new { sim = other, reason = "Employee exit" }));

        // The transfer target is required only for a transfer.
        await _k.ExpectFieldAsync("40: a transfer without a target", "transferTo", () => _k.CreateAsync(_k.Requester, "sim-return", new { sim = _simAllocated, reason = "Transfer" }));

        // Exit: a transfer target sent for another reason is ignored and not stored.
        var exit = await _k.CreateAsync(_k.Requester, "sim-return",
            new { sim = _simAllocated, reason = "Employee exit", transferTo = await _k.EmployeeIdAsync("E0009") });
        await AssertStepsAsync(exit, "sim-return");
        _k.Check(await StoredPayloadValueAsync(exit, "transferTo") is null, "40: a hidden transfer target was stored");
        await _k.ExpectRefusedAsync("40: Admin returning the SIM for the employee", _k.Admin, exit, () => _k.ActAsync(_k.Admin, exit, RequestAction.Complete));
        await DoAsync(_k.Requester, exit, "sim-return", RequestAction.Complete);
        await DoAsync(_k.Admin, exit, "admin-verification", RequestAction.Approve);
        await DoAsync(_k.Admin, exit, "telecom-deactivation", RequestAction.Complete);
        _k.Check((await SimRowAsync(_simAllocated)).Status == SimStatuses.Allocated, "40: the SIM changed before the master update step");
        await DoAsync(_k.Admin, exit, "sim-master-update", RequestAction.Complete);
        await ClosedAsync(exit, "40: the SIM return");
        var row = await SimRowAsync(_simAllocated);
        _k.Check(row.Status == SimStatuses.Available && row.HolderEmployeeId is null, $"40: the returned SIM is {row.Status}/{row.HolderEmployeeId}");
        var history = await LastHistoryAsync(MasterTypes.Sim, _simAllocated);
        _k.Check(history.EventType == MasterEvents.Returned && history.RequestId == exit, $"40: the SIM history row is {history.EventType}");
        _k.Check(await _k.Lookups.ExistsAsync(MasterLookupKinds.AvailableSim, _simAllocated, ct), "40: the returned SIM is not offered as available again");

        // Transfer to a colleague: the SIM stays allocated and moves to the target.
        var colleague = await _k.EmployeeIdAsync("E0009");
        var mine = await _k.ScalarAsync<long>("SELECT id FROM sims WHERE status = 'Allocated' AND holder_employee_id = @Demo ORDER BY id LIMIT 1", new { Demo = _demo });
        var transfer = await _k.CreateAsync(_k.Requester, "sim-return", new { sim = mine, reason = "Transfer", transferTo = colleague });
        _k.Check(await StoredPayloadValueAsync(transfer, "transferTo") == colleague.ToString(), "40: the transfer target was not stored");
        await DoAsync(_k.Requester, transfer, "sim-return", RequestAction.Complete);
        await DoAsync(_k.Admin, transfer, "admin-verification", RequestAction.Approve);
        await DoAsync(_k.Admin, transfer, "telecom-deactivation", RequestAction.Complete);
        await DoAsync(_k.Admin, transfer, "sim-master-update", RequestAction.Complete);
        await ClosedAsync(transfer, "40: the SIM transfer");
        var moved = await SimRowAsync(mine);
        _k.Check(moved.Status == SimStatuses.Allocated && moved.HolderEmployeeId == colleague, $"40: the transferred SIM is {moved.Status}/{moved.HolderEmployeeId}");
        _k.Check((await LastHistoryAsync(MasterTypes.Sim, mine)).EventType == MasterEvents.Transferred, "40: the SIM transfer left no Transferred history");
        _k.Pass("40 SIM return and transfer: show-when target, refusal for a SIM held by someone else, master release and move");
    }

    // ------------------------------------------------- 41 laptop and asset return

    // A laptop request walked to closure; returns the allocated asset.
    private async Task<(long Request, long Asset)> LaptopAsync(CancellationToken ct)
    {
        var id = await _k.CreateAsync(_k.Requester, "laptop", new { assetType = "Laptop", requirement = "Development work" });
        await AssertStepsAsync(id, "laptop");
        await DoAsync(_k.Manager, id, "manager-approval", RequestAction.Approve);
        await _k.ExpectRefusedAsync("41: Store verifying an IT asset", _k.Store, id, () => _k.ActAsync(_k.Store, id, RequestAction.Approve));
        await DoAsync(_k.It, id, "it-admin-verification", RequestAction.Approve);
        await StepStateAsync(id, "finance-approval", "NotRequired", "41: a laptop with no cost");
        await DoAsync(_k.It, id, "asset-availability", RequestAction.Complete);

        var asset = await FirstAvailableAsync(MasterLookupKinds.AvailableAsset);
        await _k.ExpectRefusedAsync("41: Store allocating an asset", _k.Store, id, () => _k.ActAsync(_k.Store, id, RequestAction.Complete, null, new() { ["asset"] = J(asset) }));
        var held = await _k.ScalarAsync<long>("SELECT id FROM assets WHERE status = 'Allocated' ORDER BY id LIMIT 1");
        await _k.ExpectFieldAsync("41: allocating an asset that is held", "asset", () => _k.ActAsync(_k.It, id, RequestAction.Complete, null, new() { ["asset"] = J(held) }));
        await _k.ExpectFieldAsync("41: allocating without choosing an asset", "asset", () => _k.ActAsync(_k.It, id, RequestAction.Complete));
        await DoAsync(_k.It, id, "asset-allocation", RequestAction.Complete, new() { ["asset"] = J(asset) });
        await DoAsync(_k.Requester, id, "employee-acknowledgement", RequestAction.Complete);
        _k.Check((await AssetRowAsync(asset)).Status == AssetStatuses.Available, "41: the asset changed before the master update step");
        await DoAsync(_k.Admin, id, "asset-master-update", RequestAction.Complete);
        await ClosedAsync(id, "41: the laptop request");

        var row = await AssetRowAsync(asset);
        _k.Check(row.Status == AssetStatuses.Allocated && row.HolderEmployeeId == _demo, $"41: the allocated asset is {row.Status}/{row.HolderEmployeeId}");
        var history = await LastHistoryAsync(MasterTypes.Asset, asset);
        _k.Check(history.EventType == MasterEvents.Allocated && history.RequestId == id, $"41: the asset history row is {history.EventType}");
        _k.Check(!await _k.Lookups.ExistsAsync(MasterLookupKinds.AvailableAsset, asset, ct), "41: the allocated asset is still offered as available");
        return (id, asset);
    }

    private async Task LaptopAndAssetReturnAsync(CancellationToken ct)
    {
        // A cost above the limit sends the laptop request to Finance too.
        var costly = await _k.CreateAsync(_k.Requester, "laptop", new { assetType = "Desktop", requirement = "Design work", estimatedCost = 85000 });
        await DoAsync(_k.Manager, costly, "manager-approval", RequestAction.Approve);
        await DoAsync(_k.Admin, costly, "it-admin-verification", RequestAction.Approve);
        await AtAsync(costly, "finance-approval", "41: a laptop with a cost");
        await StepStateAsync(costly, "finance-approval", "Pending", "41: a laptop with a cost");
        await _k.ExpectRefusedAsync("41: IT approving the Finance step", _k.It, costly, () => _k.ActAsync(_k.It, costly, RequestAction.Approve));
        await DoAsync(_k.Finance, costly, "finance-approval", RequestAction.Approve);
        await _k.ActAsync(_k.Requester, costly, RequestAction.Cancel, "Probe check finished");

        var (_, asset) = await LaptopAsync(ct);
        _k.Pass("41 laptop request: optional Finance step, allocation and master update");

        // Good condition: no damage step, asset available again.
        var good = await _k.CreateAsync(_k.Requester, "asset-return", new { asset, reason = "Exit" });
        await AssertStepsAsync(good, "asset-return");
        await DoAsync(_k.It, good, "physical-verification", RequestAction.Approve);
        await _k.ExpectFieldAsync("42: a condition outside the list", "condition", () => _k.ActAsync(_k.It, good, RequestAction.Complete, null, new() { ["condition"] = J("Broken") }));
        await _k.ExpectFieldAsync("42: no condition", "condition", () => _k.ActAsync(_k.It, good, RequestAction.Complete));
        await DoAsync(_k.It, good, "condition-check", RequestAction.Complete, new() { ["condition"] = J("Good") });
        await StepStateAsync(good, "damage-loss-calculation", "NotRequired", "42: a good return");
        await DoAsync(_k.Admin, good, "clearance", RequestAction.Approve);
        await DoAsync(_k.It, good, "asset-master-update", RequestAction.Complete);
        await ClosedAsync(good, "42: the good asset return");
        var back = await AssetRowAsync(asset);
        _k.Check(back.Status == AssetStatuses.Available && back.HolderEmployeeId is null && back.ItemCondition == ItemConditions.Good,
            $"42: the returned asset is {back.Status}/{back.HolderEmployeeId}/{back.ItemCondition}");
        var goodHistory = await LastHistoryAsync(MasterTypes.Asset, asset);
        _k.Check(goodHistory.EventType == MasterEvents.Returned && goodHistory.CostMinor is null && goodHistory.ItemCondition == ItemConditions.Good && goodHistory.RequestId == good,
            "42: the good return history row is wrong");

        // Damaged condition: the damage step is needed and its cost reaches the history.
        var (_, second) = await LaptopAsync(ct);
        var damaged = await _k.CreateAsync(_k.Requester, "asset-return", new { asset = second, reason = "Transfer" });
        await DoAsync(_k.Admin, damaged, "physical-verification", RequestAction.Approve);
        await DoAsync(_k.It, damaged, "condition-check", RequestAction.Complete, new() { ["condition"] = J("Damaged") });
        await AtAsync(damaged, "damage-loss-calculation", "42: a damaged return");
        await StepStateAsync(damaged, "damage-loss-calculation", "Pending", "42: a damaged return");
        await _k.ExpectFieldAsync("42: no damage cost", "damageCost", () => _k.ActAsync(_k.It, damaged, RequestAction.Complete));
        await _k.ExpectFieldAsync("42: a negative damage cost", "damageCost", () => _k.ActAsync(_k.It, damaged, RequestAction.Complete, null, new() { ["damageCost"] = J(-5) }));
        await DoAsync(_k.It, damaged, "damage-loss-calculation", RequestAction.Complete, new() { ["damageCost"] = J(2500.5m) });
        await DoAsync(_k.Admin, damaged, "clearance", RequestAction.Approve);
        await DoAsync(_k.It, damaged, "asset-master-update", RequestAction.Complete);
        await ClosedAsync(damaged, "42: the damaged asset return");
        var broken = await AssetRowAsync(second);
        _k.Check(broken.Status == AssetStatuses.Damaged && broken.HolderEmployeeId is null && broken.ItemCondition == ItemConditions.Damaged,
            $"42: the damaged asset is {broken.Status}/{broken.HolderEmployeeId}/{broken.ItemCondition}");
        var damagedHistory = await LastHistoryAsync(MasterTypes.Asset, second);
        _k.Check(damagedHistory.CostMinor == 250050 && damagedHistory.ItemCondition == ItemConditions.Damaged && damagedHistory.RequestId == damaged,
            $"42: the damaged return history cost is {damagedHistory.CostMinor}");
        var viaApi = (await _masters.GetHistoryAsync(MasterTypes.Asset, second, default)).First();
        _k.Check(viaApi.Cost == 2500.50m && viaApi.Condition == ItemConditions.Damaged, $"42: the history API shows cost {viaApi.Cost}");
        _k.Pass("42 asset return: conditional damage step, release or damaged status and recorded cost");
    }

    // ------------------------------------------------------------------ 43 ID card

    private async Task IdCardAsync(CancellationToken ct)
    {
        await _k.ExpectFieldAsync("43: a replacement without a reason", "reason",
            () => _k.CreateAsync(_k.Requester, "id-card", new { requestType = "Replacement", oldCardStatus = "Lost" }));
        await _k.ExpectFieldAsync("43: a replacement without the old card status", "oldCardStatus",
            () => _k.CreateAsync(_k.Requester, "id-card", new { requestType = "Replacement", reason = "Lost on a trip" }));

        // Joining needs neither; values sent anyway are ignored and not stored.
        var joining = await _k.CreateAsync(_k.Requester, "id-card", new { requestType = "Joining", details = "Priya Nair, Analyst", reason = "ignored", oldCardStatus = "Lost" });
        await AssertStepsAsync(joining, "id-card");
        _k.Check(await StoredPayloadValueAsync(joining, "reason") is null && await StoredPayloadValueAsync(joining, "oldCardStatus") is null,
            "43: hidden replacement values were stored for a joining request");
        _k.Check(await StoredPayloadValueAsync(joining, "details") == "Priya Nair, Analyst", "43: the card details were not stored");
        await _k.ActAsync(_k.Requester, joining, RequestAction.Cancel, "Probe check finished");

        var oldCard = await _k.ScalarAsync<long>("SELECT id FROM id_cards WHERE employee_id = @Demo AND status = 'Active'", new { Demo = _demo });
        var id = await _k.CreateAsync(_k.Requester, "id-card",
            new { requestType = "Replacement", details = "Priya Nair, Analyst", reason = "Lost on a trip", oldCardStatus = "Lost" });
        await _k.ExpectRefusedAsync("43: Admin doing the HR verification", _k.Admin, id, () => _k.ActAsync(_k.Admin, id, RequestAction.Approve));
        await DoAsync(_k.Hr, id, "hr-verification", RequestAction.Approve);
        await _k.ExpectFieldAsync("43: printing without a card number", "newCardNumber", () => _k.ActAsync(_k.Admin, id, RequestAction.Complete));
        await _k.ExpectFieldAsync("43: a negative replacement cost", "replacementCost",
            () => _k.ActAsync(_k.Admin, id, RequestAction.Complete, null, new() { ["newCardNumber"] = J("IDC-RPL-001"), ["replacementCost"] = J(-1) }));
        await DoAsync(_k.Admin, id, "admin-printing", RequestAction.Complete, new() { ["newCardNumber"] = J("IDC-RPL-001"), ["replacementCost"] = J(150) });
        await DoAsync(_k.Admin, id, "employee-handover", RequestAction.Complete);
        await _k.ExpectRefusedAsync("43: Admin acknowledging the card for the employee", _k.Admin, id, () => _k.ActAsync(_k.Admin, id, RequestAction.Complete));
        await DoAsync(_k.Requester, id, "acknowledgement", RequestAction.Complete);
        _k.Check(await _k.ScalarAsync<string>("SELECT status FROM id_cards WHERE id = @Id", new { Id = oldCard }) == IdCardStatuses.Active,
            "43: the old card changed before the master update step");
        await DoAsync(_k.Admin, id, "id-card-master-update", RequestAction.Complete);
        await ClosedAsync(id, "43: the ID card request");

        _k.Check(await _k.ScalarAsync<string>("SELECT status FROM id_cards WHERE id = @Id", new { Id = oldCard }) == IdCardStatuses.Replaced,
            "43: the old card was not marked Replaced");
        var active = await _k.QueryAsync<long>("SELECT id FROM id_cards WHERE employee_id = @Demo AND status = 'Active' AND card_number = 'IDC-RPL-001'", new { Demo = _demo });
        _k.Check(active.Count == 1 && await _k.ScalarAsync<long>("SELECT COUNT(*) FROM id_cards WHERE employee_id = @Demo AND status = 'Active'", new { Demo = _demo }) == 1,
            "43: the employee does not hold exactly the one new active card");
        var history = await LastHistoryAsync(MasterTypes.IdCard, active[0]);
        _k.Check(history.RequestId == id && history.CostMinor == 15000 && history.Notes is not null && history.Notes.Contains("Lost on a trip") && history.Notes.Contains("Old card: Lost"),
            $"43: the new card history is wrong ({history.Notes}, {history.CostMinor})");
        _k.Pass("43 ID card: replacement-only fields, printing capture and replacement of the active card");
    }

    // -------------------------------------------- 44 welfare and housekeeping

    private async Task WelfareAndHousekeepingAsync(CancellationToken ct)
    {
        await _k.ExpectFieldAsync("44: a welfare category outside the list", "category",
            () => _k.CreateAsync(_k.Requester, "welfare", new { category = "Holiday", details = "x" }));
        var welfare = await _k.CreateAsync(_k.Requester, "welfare", new { category = "Medical camp arrangements", details = "Annual health check-up camp" });
        await AssertStepsAsync(welfare, "welfare");
        await _k.ExpectRefusedAsync("44: the manager doing the Admin review", _k.Manager, welfare, () => _k.ActAsync(_k.Manager, welfare, RequestAction.Approve));
        await DoAsync(_k.Admin, welfare, "admin-review", RequestAction.Approve);
        await _k.ExpectRefusedAsync("44: Admin giving the manager approval", _k.Admin, welfare, () => _k.ActAsync(_k.Admin, welfare, RequestAction.Approve));
        await DoAsync(_k.Manager, welfare, "approval", RequestAction.Approve);
        await DoAsync(_k.Admin, welfare, "action", RequestAction.Complete);
        await _k.ExpectFieldAsync("44: an expense entry without an amount", "expenseAmount", () => _k.ActAsync(_k.Admin, welfare, RequestAction.Complete));
        await DoAsync(_k.Admin, welfare, "expense-entry", RequestAction.Complete, new() { ["expenseAmount"] = J(1500), ["expenseNote"] = J("Camp supplies") });
        await DoAsync(_k.Requester, welfare, "employee-confirmation", RequestAction.Complete);
        await ClosedAsync(welfare, "44: the welfare request");
        var captured = (await _k.StepAsync(welfare, "expense-entry")).CapturedJson ?? string.Empty;
        _k.Check(captured.Contains("150000") && captured.Contains("Camp supplies"), $"44: the expense was not recorded ({captured})");

        var location = await _k.ScalarAsync<long>("SELECT id FROM locations WHERE is_active = 1 ORDER BY id LIMIT 1");
        await _k.ExpectFieldAsync("44: housekeeping without a location", "location",
            () => _k.CreateAsync(_k.Requester, "housekeeping", new { category = "Pantry", description = "Tea station is dirty" }));
        var house = await _k.CreateAsync(_k.Requester, "housekeeping",
            new { location, area = "Second floor", category = "Pantry", description = "Tea station is dirty" });
        await AssertStepsAsync(house, "housekeeping");
        _k.Check((await _k.StepsAsync(house)).All(s => s.StepType == "Task"), "44: housekeeping has an approval step");
        await _k.ExpectRefusedAsync("44: the requester assigning the request", _k.Requester, house, () => _k.ActAsync(_k.Requester, house, RequestAction.Complete, null, new() { ["assignedTo"] = J("Vendor") }));
        await _k.ExpectFieldAsync("44: assigning without a name", "assignedTo", () => _k.ActAsync(_k.Admin, house, RequestAction.Complete));
        await DoAsync(_k.Admin, house, "admin-assignment", RequestAction.Complete, new() { ["assignedTo"] = J("Housekeeping vendor") });
        await DoAsync(_k.Admin, house, "housekeeping-action", RequestAction.Complete);
        await DoAsync(_k.Admin, house, "completion", RequestAction.Complete);
        await _k.ExpectRefusedAsync("44: Admin confirming for the employee", _k.Admin, house, () => _k.ActAsync(_k.Admin, house, RequestAction.Complete));
        await DoAsync(_k.Requester, house, "employee-confirmation", RequestAction.Complete);
        await ClosedAsync(house, "44: the housekeeping request");
        _k.Pass("44 welfare and housekeeping walked to closure with the section's steps");
    }

    // ------------------------------------------------------------------ 45 courier

    private async Task CourierAsync()
    {
        var courier = await _k.Definitions.GetActiveAsync("courier");
        _k.Check(courier is not null && courier.Definition.Version == 4, "45: the active courier definition is not version 4");

        var id = await _k.CreateAsync(_k.Requester, "courier", new
        {
            documentDescription = "Signed contract",
            senderName = "Priya Nair",
            receiverName = "Receiver Modules",
            receiverAddress = "2 Example Road",
            receiverCity = "Pune"
        });
        await AssertStepsAsync(id, "courier");
        await DoAsync(_k.Admin, id, "courier-selection", RequestAction.Complete, new() { ["courierCompany"] = J("Example Couriers") });
        await DoAsync(_k.Admin, id, "dispatch", RequestAction.Complete);
        await DoAsync(_k.Admin, id, "tracking-number", RequestAction.Complete, new() { ["trackingNumber"] = J("TRK-MOD-1") });
        await DoAsync(_k.Requester, id, "delivery-confirmation", RequestAction.Complete);
        await AtAsync(id, "pod-upload", "45: courier");
        var missing = await _k.ExpectAsync<ValidationException>("45: completing the proof of delivery without a document",
            () => _k.ActAsync(_k.Admin, id, RequestAction.Complete));
        _k.Check(missing.Code == ErrorCodes.DOCUMENT_REQUIRED, $"45: the missing document was reported as {missing.Code}");
        await _k.ActAsync(_k.Admin, id, RequestAction.Reject, "Probe check finished");
        _k.Pass("45 courier version 4 reaches the proof of delivery step and needs a document");
    }
}
