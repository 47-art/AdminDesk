using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.Infrastructure.Masters;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Requests;

// Checks 35 to 38: the seeded SIM, asset and ID card masters, the lookups, the request hook that applies
// allocation, return and replacement, and the owner maintenance rules with their audit rows and policies.
// The hook is driven directly inside a transaction that is rolled back afterwards, so the seeded records
// are left as they were.
internal sealed class EngineProbeMasterChecks
{
    private sealed class RollBack : Exception
    {
    }

    private readonly ProbeKit _k;
    private readonly IMasterAssetService _service;
    private readonly IMasterAssetRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRequestRepository _requests;
    private readonly AssetMasterHook _hook;

    private long _demo;
    private long _baseRequest;

    public EngineProbeMasterChecks(ProbeKit kit)
    {
        _k = kit;
        _service = kit.Services.GetRequiredService<IMasterAssetService>();
        _repository = kit.Services.GetRequiredService<IMasterAssetRepository>();
        _unitOfWork = kit.Services.GetRequiredService<IUnitOfWork>();
        _requests = kit.Services.GetRequiredService<IRequestRepository>();
        _hook = kit.Services.GetServices<IRequestHook>().OfType<AssetMasterHook>().Single();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _demo = await _k.EmployeeIdAsync("E0010");
        _baseRequest = _k.Created.First();

        await SeedAsync();
        await LookupsAsync(ct);
        await HookGuardAsync(ct);
        await HookEffectsAsync(ct);
        await MaintenanceAsync(ct);
        await PoliciesAsync();
    }

    // ------------------------------------------------------------------ helpers

    private async Task SandboxAsync(Func<DbConnection, DbTransaction, Task> work, CancellationToken ct)
    {
        try
        {
            await _unitOfWork.ExecuteInTransactionAsync<int>(async (connection, tx) =>
            {
                await work(connection, tx);
                throw new RollBack();
            }, ct);
        }
        catch (RollBack)
        {
        }
    }

    private async Task<HookContext> ContextAsync(
        DbConnection connection, DbTransaction tx, string module, object payload, CancellationToken ct)
    {
        var snapshot = await _requests.GetSnapshotAsync(tx, _baseRequest, ct) ?? throw new ProbeFailure("The base request is missing.");
        snapshot = snapshot with
        {
            ModuleCode = module,
            RequesterEmployeeId = checked((int)_demo),
            PayloadJson = JsonSerializer.Serialize(payload)
        };
        return new HookContext(connection, tx, _k.Admin, snapshot);
    }

    private static Task AddStepAsync(DbConnection connection, DbTransaction tx, long requestId, int seq, string key, object captured) =>
        connection.ExecuteAsync(
            "INSERT INTO request_steps(request_id, seq, step_key, name, step_type, state, captured_json) " +
            "VALUES(@Id, @Seq, @Key, @Key, 'Task', 'Done', @Json)",
            new { Id = requestId, Seq = seq, Key = key, Json = JsonSerializer.Serialize(captured) }, tx);

    private static StepDoneInfo Done(string stepKey) =>
        new(stepKey, RequestAction.Complete, new Dictionary<string, object?>());

    private Task<T> ScalarInAsync<T>(DbConnection connection, DbTransaction tx, string sql, object? p = null) =>
        connection.ExecuteScalarAsync<T>(sql, p, tx)!;

    private async Task<long> OneIdAsync(string sql, object? p = null) =>
        await _k.ScalarAsync<long>(sql, p);

    private static JsonNode? Details(string? json) => json is null ? null : JsonNode.Parse(json);

    // ------------------------------------------------------------------ 35 seed

    private async Task SeedAsync()
    {
        var sims = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM sims WHERE is_active = 1");
        var assets = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM assets WHERE is_active = 1");
        var cards = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM id_cards WHERE is_active = 1");
        _k.Check(sims >= 25 && assets >= 35 && cards >= 15, $"35: seeded {sims} SIMs, {assets} assets and {cards} ID cards");

        foreach (var (type, table) in new[] { (MasterTypes.Sim, "sims"), (MasterTypes.Asset, "assets"), (MasterTypes.IdCard, "id_cards") })
        {
            var without = await _k.ScalarAsync<long>(
                $"SELECT COUNT(*) FROM {table} t WHERE NOT EXISTS (SELECT 1 FROM master_history h WHERE h.master_type = @Type AND h.master_id = t.id)",
                new { Type = type });
            _k.Check(without == 0, $"35: {without} seeded {type} records have no history row");
        }

        var allocatedSims = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM sims WHERE status = 'Allocated' AND holder_employee_id IS NOT NULL");
        var availableSims = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM sims WHERE status = 'Available' AND holder_employee_id IS NULL");
        var damaged = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM assets WHERE status = 'Damaged'");
        _k.Check(allocatedSims >= 10 && availableSims >= 10 && damaged >= 1, "35: the seeded SIMs and assets are not a mix of allocated, available and damaged");

        var holdings = await _service.GetHoldingsAsync(_demo, default);
        _k.Check(holdings.Sims.Count >= 1 && holdings.Assets.Count >= 1 && holdings.IdCard is not null,
            "35: the demo employee does not hold a SIM, an asset and an ID card");
        var foreign = await _k.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sims WHERE id IN @Ids AND holder_employee_id <> @Demo", new { Ids = holdings.Sims.Select(s => s.Id).ToArray(), Demo = _demo });
        _k.Check(foreign == 0, "35: holdings list a SIM somebody else holds");

        var history = await _service.GetHistoryAsync(MasterTypes.Sim, holdings.Sims[0].Id, default);
        _k.Check(history.Count >= 1 && history[0].EventType == MasterEvents.Allocated, "35: history of a held SIM does not show the allocation");
        await _k.ExpectAsync<NotFoundException>("35: history of an unknown record", () => _service.GetHistoryAsync(MasterTypes.Sim, 999999, default));

        var page = await _service.ListSimsAsync(1, 10, null, "Available", null, default);
        _k.Check(page.Items.Count == 10 && page.Total >= 10 && page.Items.All(s => s.Status == SimStatuses.Available && s.HolderName is null),
            "35: the status filter of the SIM list is wrong");
        var byHolder = await _service.ListAssetsAsync(1, 50, null, null, _demo, default);
        _k.Check(byHolder.Items.Count >= 1 && byHolder.Items.All(a => a.HolderEmployeeId == _demo && a.HolderCode == "E0010"),
            "35: the holder filter of the asset list is wrong");
        var bySearch = await _service.ListIdCardsAsync(1, 10, "IDC-0001", null, null, default);
        _k.Check(bySearch.Items.Any(c => c.CardNumber == "IDC-0001" && c.EmployeeName is not null), "35: searching ID cards by number found nothing");
        await _k.ExpectAsync<ValidationException>("35: unknown status filter", () => _service.ListSimsAsync(1, 10, null, "Nonsense", null, default));
        _k.Pass("35 seeded masters, history, lists and holdings");
    }

    // ---------------------------------------------------------------- 36 lookups

    private async Task CheckInactiveLabelAsync(string kind, long id, string name, CancellationToken ct)
    {
        var provider = _k.Lookups.Find(kind)!;
        _k.Check(await provider.GetAsync(id, ct) is not null, $"36: the {kind} label of a deactivated item was not resolved");
        _k.Check((await provider.GetManyAsync(new[] { id }, ct)).Count == 1, $"36: the {kind} labels of a deactivated item were not resolved");
        _k.Check(!await provider.ExistsAsync(id, ct), $"36: a deactivated {kind} still passed validation");
        _k.Check((await provider.SearchAsync(name, 50, ct)).All(i => i.Id != id), $"36: a deactivated {kind} was offered in search");
    }

    private async Task LookupsAsync(CancellationToken ct)
    {
        async Task<List<LookupItem>> SearchAllAsync(string kind) =>
            (await _k.Lookups.Find(kind)!.SearchAsync(string.Empty, 200, ct)).ToList();

        var availableSims = await SearchAllAsync(MasterLookupKinds.AvailableSim);
        var heldSims = await SearchAllAsync(MasterLookupKinds.HeldSim);
        var availableAssets = await SearchAllAsync(MasterLookupKinds.AvailableAsset);
        var heldAssets = await SearchAllAsync(MasterLookupKinds.HeldAsset);

        var availableSimIds = await _k.QueryAsync<long>("SELECT id FROM sims WHERE status = 'Available' AND is_active = 1");
        var heldSimIds = await _k.QueryAsync<long>("SELECT id FROM sims WHERE status = 'Allocated' AND holder_employee_id IS NOT NULL AND is_active = 1");
        var availableAssetIds = await _k.QueryAsync<long>("SELECT id FROM assets WHERE status = 'Available' AND is_active = 1");
        var heldAssetIds = await _k.QueryAsync<long>("SELECT id FROM assets WHERE status = 'Allocated' AND holder_employee_id IS NOT NULL AND is_active = 1");

        _k.Check(availableSims.Select(i => i.Id).OrderBy(i => i).SequenceEqual(availableSimIds.OrderBy(i => i)), "36: availableSim does not list exactly the Available SIMs");
        _k.Check(heldSims.Select(i => i.Id).OrderBy(i => i).SequenceEqual(heldSimIds.OrderBy(i => i)), "36: heldSim does not list exactly the held SIMs");
        _k.Check(availableAssets.Select(i => i.Id).OrderBy(i => i).SequenceEqual(availableAssetIds.OrderBy(i => i)), "36: availableAsset does not list exactly the Available assets");
        _k.Check(heldAssets.Select(i => i.Id).OrderBy(i => i).SequenceEqual(heldAssetIds.OrderBy(i => i)), "36: heldAsset does not list exactly the held assets");
        _k.Check(heldSims.All(i => !string.IsNullOrEmpty(i.Secondary)) && heldAssets.All(i => !string.IsNullOrEmpty(i.Secondary)),
            "36: held lookups do not show the holder");
        _k.Check(availableSims.All(i => i.Label.Contains(" - ")) && availableAssets.All(i => i.Label.Contains(" - ")), "36: lookup labels are not number with mobile or model");

        var filtered = await _k.Lookups.Find(MasterLookupKinds.AvailableAsset)!.SearchAsync("Latitude", 50, ct);
        _k.Check(filtered.Count >= 1 && filtered.All(i => i.Label.Contains("Latitude")), "36: searching the asset lookup by model failed");

        // An empty result must come back as an empty list, not as a failure.
        foreach (var kind in new[] { MasterLookupKinds.AvailableSim, MasterLookupKinds.HeldSim, MasterLookupKinds.AvailableAsset, MasterLookupKinds.HeldAsset })
        {
            var provider = _k.Lookups.Find(kind)!;
            try
            {
                var none = await provider.SearchAsync("no-such-item-zzz", 50, ct);
                _k.Check(none.Count == 0, $"36: searching {kind} for text that matches nothing returned items");
                _k.Check((await provider.GetManyAsync(new long[] { 987654321 }, ct)).Count == 0 && await provider.GetAsync(987654321, ct) is null,
                    $"36: looking up an unknown id in {kind} returned an item");
            }
            catch (Exception exception)
            {
                _k.Check(false, $"36: an empty {kind} lookup failed: {exception.Message}");
            }
        }

        // Labels still resolve for an item that was deactivated; search and validation skip it. A seeded row is
        // deactivated for the checks and switched back on afterwards.
        foreach (var (kind, table) in new[] { ("department", "departments"), ("location", "locations"), ("project", "projects"), ("employee", "employees") })
        {
            var id = await _k.ScalarAsync<long>($"SELECT MAX(id) FROM {table} WHERE is_active = 1");
            var name = await _k.ScalarAsync<string>($"SELECT {(table == "employees" ? "full_name" : "name")} FROM {table} WHERE id = {id}");
            await _k.ExecuteRawAsync($"UPDATE {table} SET is_active = 0, deleted_utc = '2026-01-02T00:00:00Z' WHERE id = {id}");
            try
            {
                await CheckInactiveLabelAsync(kind, id, name, ct);
            }
            finally
            {
                await _k.ExecuteRawAsync($"UPDATE {table} SET is_active = 1, deleted_utc = NULL WHERE id = {id}");
            }
        }

        var nothingHeld =await _service.GetHoldingsAsync(987654321, ct);
        _k.Check(nothingHeld.Sims.Count == 0 && nothingHeld.Assets.Count == 0 && nothingHeld.IdCard is null,
            "36: the holdings of an employee who holds nothing were not empty");

        var many = await _k.Lookups.Find(MasterLookupKinds.HeldSim)!.GetManyAsync(heldSimIds.Concat(availableSimIds).ToArray(), ct);
        // Label lookups return every item asked for, so a request keeps showing the label of an item that was
        // allocated or returned since. Eligibility applies to search and validation only.
        _k.Check(many.Count == heldSimIds.Count + availableSimIds.Count, "36: GetManyAsync of heldSim did not return every requested item");

        // An item taken in the meantime no longer validates.
        var sim = availableSimIds[0];
        var availableKind = _k.Lookups.Find(MasterLookupKinds.AvailableSim)!;
        _k.Check(await availableKind.ExistsAsync(sim, ct) && await _k.Lookups.ExistsAsync(MasterLookupKinds.AvailableSim, sim, ct), "36: an available SIM does not validate");
        await _k.ExecuteRawAsync($"UPDATE sims SET status = 'Allocated', holder_employee_id = {_demo} WHERE id = {sim}");
        _k.Check(!await availableKind.ExistsAsync(sim, ct), "36: a SIM taken meanwhile still validates as available");
        _k.Check(await _k.Lookups.Find(MasterLookupKinds.HeldSim)!.ExistsAsync(sim, ct), "36: a held SIM does not validate as held");
        await _k.ExecuteRawAsync($"UPDATE sims SET status = 'Available', holder_employee_id = NULL WHERE id = {sim}");
        _k.Check(!await _k.Lookups.ExistsAsync(MasterLookupKinds.HeldSim, sim, ct) && !await _k.Lookups.ExistsAsync(MasterLookupKinds.AvailableAsset, 999999, ct),
            "36: held validation or an unknown id gave the wrong answer");
        _k.Pass("36 lookups offer only eligible items");
    }

    // ------------------------------------------------------- 37a creation guard

    private async Task HookGuardAsync(CancellationToken ct)
    {
        var mySim = await OneIdAsync("SELECT id FROM sims WHERE holder_employee_id = @Demo AND status = 'Allocated' LIMIT 1", new { Demo = _demo });
        var otherSim = await OneIdAsync("SELECT id FROM sims WHERE holder_employee_id <> @Demo AND status = 'Allocated' LIMIT 1", new { Demo = _demo });
        var freeSim = await OneIdAsync("SELECT id FROM sims WHERE status = 'Available' LIMIT 1");
        var myAsset = await OneIdAsync("SELECT id FROM assets WHERE holder_employee_id = @Demo AND status = 'Allocated' LIMIT 1", new { Demo = _demo });
        var otherAsset = await OneIdAsync("SELECT id FROM assets WHERE holder_employee_id <> @Demo AND status = 'Allocated' LIMIT 1", new { Demo = _demo });
        var colleague = await _k.EmployeeIdAsync("E0009");

        await SandboxAsync(async (connection, tx) =>
        {
            async Task RefusedAsync(string module, object payload, string field, string what)
            {
                var context = await ContextAsync(connection, tx, module, payload, ct);
                var ex = await _k.ExpectAsync<ValidationException>(what, () => _hook.OnCreatingAsync(context, ct));
                _k.Check(ex.FieldErrors.Any(e => e.Field == field), $"37: {what} not keyed to '{field}'");
            }

            await RefusedAsync(MasterModules.SimReturn, new { sim = otherSim, reason = "Employee exit" }, "sim", "return of a SIM held by someone else");
            await RefusedAsync(MasterModules.SimReturn, new { sim = freeSim, reason = "Employee exit" }, "sim", "return of a SIM nobody holds");
            await RefusedAsync(MasterModules.SimReturn, new { reason = "Employee exit" }, "sim", "return without a SIM");
            await RefusedAsync(MasterModules.AssetReturn, new { asset = otherAsset }, "asset", "return of an asset held by someone else");
            await RefusedAsync(MasterModules.SimReturn, new { sim = mySim, reason = "Transfer" }, "transferTo", "transfer without a target");
            await RefusedAsync(MasterModules.SimReturn, new { sim = mySim, reason = "Transfer", transferTo = _demo }, "transferTo", "transfer to oneself");

            foreach (var (module, payload) in new (string, object)[]
                     {
                         (MasterModules.SimReturn, new { sim = mySim, reason = "Employee exit" }),
                         (MasterModules.SimReturn, new { sim = mySim, reason = "Transfer", transferTo = colleague }),
                         (MasterModules.AssetReturn, new { asset = myAsset }),
                         (MasterModules.Sim, new { sim = otherSim }),
                         ("stationery", new { asset = otherAsset })
                     })
            {
                await _hook.OnCreatingAsync(await ContextAsync(connection, tx, module, payload, ct), ct);
            }
        }, ct);
        _k.Pass("37 creation guard refuses returns of items the requester does not hold");
    }

    // ------------------------------------------------------ 37b step effects

    private async Task HookEffectsAsync(CancellationToken ct)
    {
        var colleague = await _k.EmployeeIdAsync("E0009");
        var mySim = await OneIdAsync("SELECT id FROM sims WHERE holder_employee_id = @Demo AND status = 'Allocated' LIMIT 1", new { Demo = _demo });
        var freeSim = await OneIdAsync("SELECT id FROM sims WHERE status = 'Available' ORDER BY id LIMIT 1");
        var myAsset = await OneIdAsync("SELECT id FROM assets WHERE holder_employee_id = @Demo AND status = 'Allocated' LIMIT 1", new { Demo = _demo });
        var freeAsset = await OneIdAsync("SELECT id FROM assets WHERE status = 'Available' ORDER BY id LIMIT 1");
        var myCard = await OneIdAsync("SELECT id FROM id_cards WHERE employee_id = @Demo AND status = 'Active' LIMIT 1", new { Demo = _demo });

        var simsBefore = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM sims");
        var historyBefore = await _k.ScalarAsync<long>("SELECT COUNT(*) FROM master_history");

        // SIM exit: back to Available with no holder.
        await SandboxAsync(async (c, tx) =>
        {
            var context = await ContextAsync(c, tx, MasterModules.SimReturn, new { sim = mySim, reason = "Employee exit" }, ct);
            await _hook.OnStepDoneAsync(context, Done(MasterModules.SimMasterUpdateStep), ct);
            var row = await c.QuerySingleAsync<(string Status, long? Holder)>("SELECT status, holder_employee_id FROM sims WHERE id = @Id", new { Id = mySim }, tx);
            _k.Check(row.Status == SimStatuses.Available && row.Holder is null, "37: a returned SIM is not Available without a holder");
            var ev = await c.QuerySingleAsync<(string Event, long? Request)>(
                "SELECT event_type, request_id FROM master_history WHERE master_type = 'Sim' AND master_id = @Id ORDER BY id DESC LIMIT 1", new { Id = mySim }, tx);
            _k.Check(ev.Event == MasterEvents.Returned && ev.Request == _baseRequest, "37: the SIM return left no history row linked to the request");
            _k.Check(await ScalarInAsync<long>(c, tx, "SELECT COUNT(*) FROM audit_events WHERE request_id = @Id AND event_type = 'MasterUpdated'", new { Id = _baseRequest }) == 1,
                "37: the SIM return left no MasterUpdated audit row");
        }, ct);

        // SIM transfer: the holder changes, the SIM stays Allocated.
        await SandboxAsync(async (c, tx) =>
        {
            var context = await ContextAsync(c, tx, MasterModules.SimReturn, new { sim = mySim, reason = "Transfer", transferTo = colleague }, ct);
            await _hook.OnStepDoneAsync(context, Done(MasterModules.SimMasterUpdateStep), ct);
            var row = await c.QuerySingleAsync<(string Status, long? Holder)>("SELECT status, holder_employee_id FROM sims WHERE id = @Id", new { Id = mySim }, tx);
            _k.Check(row.Status == SimStatuses.Allocated && row.Holder == colleague, "37: a transferred SIM did not move to the new holder");
            var ev = await c.QuerySingleAsync<(string Event, long? Employee)>(
                "SELECT event_type, employee_id FROM master_history WHERE master_type = 'Sim' AND master_id = @Id ORDER BY id DESC LIMIT 1", new { Id = mySim }, tx);
            _k.Check(ev.Event == MasterEvents.Transferred && ev.Employee == colleague, "37: the SIM transfer left the wrong history row");
        }, ct);

        // New SIM: holder, status, activation date and history named after the request type; a second try conflicts.
        await SandboxAsync(async (c, tx) =>
        {
            await AddStepAsync(c, tx, _baseRequest, 901, MasterModules.SimAllocationStep, new { sim = freeSim });
            await AddStepAsync(c, tx, _baseRequest, 902, MasterModules.TelecomActivationStep, new { activationDate = "2026-02-03" });
            var context = await ContextAsync(c, tx, MasterModules.Sim, new { requestType = "Replacement" }, ct);
            await _hook.OnStepDoneAsync(context, Done(MasterModules.SimMasterUpdateStep), ct);
            var row = await c.QuerySingleAsync<(string Status, long? Holder, string? Activation)>(
                "SELECT status, holder_employee_id, activation_date FROM sims WHERE id = @Id", new { Id = freeSim }, tx);
            _k.Check(row.Status == SimStatuses.Allocated && row.Holder == _demo && row.Activation == "2026-02-03", "37: an allocated SIM has the wrong holder, status or activation date");
            _k.Check(await ScalarInAsync<string>(c, tx,
                "SELECT event_type FROM master_history WHERE master_type = 'Sim' AND master_id = @Id ORDER BY id DESC LIMIT 1", new { Id = freeSim }) == "Replacement",
                "37: the SIM history event is not named after the request type");

            var again = await ContextAsync(c, tx, MasterModules.Sim, new { requestType = "New SIM" }, ct);
            var historyRows = await ScalarInAsync<long>(c, tx, "SELECT COUNT(*) FROM master_history WHERE master_type = 'Sim' AND master_id = @Id", new { Id = freeSim });
            var ex = await _k.ExpectAsync<ConflictException>("37: allocating a SIM that is no longer available", () => _hook.OnStepDoneAsync(again, Done(MasterModules.SimMasterUpdateStep), ct));
            _k.Check(ex.Code == ErrorCodes.STATE_CONFLICT, "37: the allocation conflict has the wrong code");
            _k.Check(await ScalarInAsync<long>(c, tx, "SELECT COUNT(*) FROM master_history WHERE master_type = 'Sim' AND master_id = @Id", new { Id = freeSim }) == historyRows,
                "37: a refused allocation wrote history");
        }, ct);

        // Laptop allocation.
        await SandboxAsync(async (c, tx) =>
        {
            await AddStepAsync(c, tx, _baseRequest, 901, MasterModules.AssetAllocationStep, new { asset = freeAsset });
            var context = await ContextAsync(c, tx, MasterModules.Laptop, new { }, ct);
            await _hook.OnStepDoneAsync(context, Done(MasterModules.AssetMasterUpdateStep), ct);
            var row = await c.QuerySingleAsync<(string Status, long? Holder)>("SELECT status, holder_employee_id FROM assets WHERE id = @Id", new { Id = freeAsset }, tx);
            _k.Check(row.Status == AssetStatuses.Allocated && row.Holder == _demo, "37: an allocated asset has the wrong holder or status");
            _k.Check(await ScalarInAsync<string>(c, tx,
                "SELECT event_type FROM master_history WHERE master_type = 'Asset' AND master_id = @Id ORDER BY id DESC LIMIT 1", new { Id = freeAsset }) == MasterEvents.Allocated,
                "37: the asset allocation left no history row");
        }, ct);

        // Asset return for each condition.
        foreach (var (condition, status, cost) in new (string, string, long?)[]
                 {
                     (ItemConditions.Good, AssetStatuses.Available, null),
                     (ItemConditions.Damaged, AssetStatuses.Damaged, 250000),
                     (ItemConditions.Lost, AssetStatuses.Lost, 4500000)
                 })
        {
            await SandboxAsync(async (c, tx) =>
            {
                await AddStepAsync(c, tx, _baseRequest, 901, MasterModules.ConditionCheckStep, new { condition });
                if (cost is not null)
                {
                    await AddStepAsync(c, tx, _baseRequest, 902, MasterModules.DamageLossStep, new { damageCost = cost });
                }
                var context = await ContextAsync(c, tx, MasterModules.AssetReturn, new { asset = myAsset }, ct);
                await _hook.OnStepDoneAsync(context, Done(MasterModules.AssetMasterUpdateStep), ct);
                var row = await c.QuerySingleAsync<(string Status, long? Holder, string? Condition)>(
                    "SELECT status, holder_employee_id, item_condition FROM assets WHERE id = @Id", new { Id = myAsset }, tx);
                _k.Check(row.Status == status && row.Holder is null && row.Condition == condition, $"37: a {condition} return left the asset as {row.Status}/{row.Condition}");
                var ev = await c.QuerySingleAsync<(string Event, string? Condition, long? Cost)>(
                    "SELECT event_type, item_condition, cost_minor FROM master_history WHERE master_type = 'Asset' AND master_id = @Id ORDER BY id DESC LIMIT 1", new { Id = myAsset }, tx);
                _k.Check(ev.Event == MasterEvents.Returned && ev.Condition == condition && ev.Cost == cost, $"37: a {condition} return wrote the wrong history row");
            }, ct);
        }

        // ID card replacement.
        await SandboxAsync(async (c, tx) =>
        {
            await AddStepAsync(c, tx, _baseRequest, 901, MasterModules.AdminPrintingStep, new { newCardNumber = "IDC-9001", replacementCost = 10000 });
            var payload = new { requestType = "Replacement", reason = "Lost card", oldCardStatus = "Lost" };
            var context = await ContextAsync(c, tx, MasterModules.IdCard, payload, ct);
            await _hook.OnStepDoneAsync(context, Done(MasterModules.IdCardMasterUpdateStep), ct);
            _k.Check(await ScalarInAsync<string>(c, tx, "SELECT status FROM id_cards WHERE id = @Id", new { Id = myCard }) == IdCardStatuses.Replaced, "37: the previous ID card was not marked Replaced");
            var active = await c.QueryAsync<(long Id, string Number)>("SELECT id, card_number FROM id_cards WHERE employee_id = @Demo AND status = 'Active'", new { Demo = _demo }, tx);
            _k.Check(active.Count() == 1 && active.Single().Number == "IDC-9001", "37: the employee does not have exactly the new active card");
            var ev = await c.QuerySingleAsync<(string Event, long? Cost, string? Notes)>(
                "SELECT event_type, cost_minor, notes FROM master_history WHERE master_type = 'IdCard' AND master_id = @Id ORDER BY id DESC LIMIT 1",
                new { Id = active.Single().Id }, tx);
            _k.Check(ev.Event == MasterEvents.Replaced && ev.Cost == 10000 && ev.Notes is not null && ev.Notes.Contains("Lost card") && ev.Notes.Contains("Lost"),
                "37: the new card history does not hold reason, old status and cost");

            var duplicate = await ContextAsync(c, tx, MasterModules.IdCard, payload, ct);
            var ex = await _k.ExpectAsync<ValidationException>("37: reusing a card number", () => _hook.OnStepDoneAsync(duplicate, Done(MasterModules.IdCardMasterUpdateStep), ct));
            _k.Check(ex.FieldErrors.Any(e => e.Field == "newCardNumber"), "37: the repeated card number is not keyed to newCardNumber");
        }, ct);

        // Steps outside the contract, other modules and approvals do nothing.
        await SandboxAsync(async (c, tx) =>
        {
            await _hook.OnStepDoneAsync(await ContextAsync(c, tx, MasterModules.SimReturn, new { sim = mySim, reason = "Employee exit" }, ct), Done("some-other-step"), ct);
            await _hook.OnStepDoneAsync(await ContextAsync(c, tx, "stationery", new { sim = mySim }, ct), Done(MasterModules.SimMasterUpdateStep), ct);
            await _hook.OnStepDoneAsync(await ContextAsync(c, tx, MasterModules.SimReturn, new { sim = mySim, reason = "Employee exit" }, ct),
                new StepDoneInfo(MasterModules.SimMasterUpdateStep, RequestAction.Approve, new Dictionary<string, object?>()), ct);
            _k.Check(await ScalarInAsync<string>(c, tx, "SELECT status FROM sims WHERE id = @Id", new { Id = mySim }) == SimStatuses.Allocated, "37: a step outside the contract changed a SIM");
        }, ct);

        _k.Check(await _k.ScalarAsync<long>("SELECT COUNT(*) FROM sims") == simsBefore &&
                 await _k.ScalarAsync<long>("SELECT COUNT(*) FROM master_history") == historyBefore, "37: the rolled back effects left rows behind");
        _k.Pass("37 request steps update holder, status, condition, cost and history");
    }

    // ------------------------------------------------------ 38 maintenance

    private async Task MaintenanceAsync(CancellationToken ct)
    {
        SimFieldsBody Sim(string n, string m) => new()
        {
            SimNumber = n, MobileNumber = m, TelecomOperator = "Jio", Plan = "Probe 199", MonthlyCost = 199m
        };
        AssetFieldsBody Asset(string tag) => new()
        {
            AssetTag = tag, AssetType = "Laptop", MakeModel = "Probe Model", SerialNumber = "PRB-" + tag
        };

        // Who may add.
        var simAdmin = await _service.AddSimAsync(_k.Admin, Sim("PRB-SIM-1", "9100000001"), ct);
        var simManagement = await _service.AddSimAsync(_k.Management, Sim("PRB-SIM-2", "9100000002"), ct);
        _k.Check(simAdmin.Status == SimStatuses.Available && simAdmin.HolderEmployeeId is null && simAdmin.MonthlyCost == 199m, "38: a new SIM does not start Available without a holder");
        foreach (var (name, actor) in new[] { ("IT", _k.It), ("HR", _k.Hr), ("SystemAdmin", _k.SysAdmin), ("Employee", _k.Requester) })
        {
            await _k.ExpectAsync<ForbiddenException>($"38: {name} adding a SIM", () => _service.AddSimAsync(actor, Sim("PRB-SIM-X", "9100000009"), ct));
        }

        var assetIt = await _service.AddAssetAsync(_k.It, Asset("PRB-AST-1"), ct);
        await _service.AddAssetAsync(_k.Admin, Asset("PRB-AST-2"), ct);
        await _service.AddAssetAsync(_k.Management, Asset("PRB-AST-3"), ct);
        foreach (var (name, actor) in new[] { ("HR", _k.Hr), ("SystemAdmin", _k.SysAdmin), ("Employee", _k.Requester) })
        {
            await _k.ExpectAsync<ForbiddenException>($"38: {name} adding an asset", () => _service.AddAssetAsync(actor, Asset("PRB-AST-X"), ct));
        }

        var free = await _k.QueryAsync<long>(
            "SELECT e.id FROM employees e WHERE NOT EXISTS (SELECT 1 FROM id_cards c WHERE c.employee_id = e.id AND c.status = 'Active') ORDER BY e.id DESC LIMIT 4");
        IdCardAddBody Card(string n, long emp) => new() { CardNumber = n, EmployeeId = emp, IssuedDate = new DateOnly(2026, 1, 15) };
        var cardHr = await _service.AddIdCardAsync(_k.Hr, Card("PRB-IDC-1", free[0]), ct);
        await _service.AddIdCardAsync(_k.Admin, Card("PRB-IDC-2", free[1]), ct);
        await _service.AddIdCardAsync(_k.Management, Card("PRB-IDC-3", free[2]), ct);
        foreach (var (name, actor) in new[] { ("IT", _k.It), ("SystemAdmin", _k.SysAdmin), ("Employee", _k.Requester) })
        {
            await _k.ExpectAsync<ForbiddenException>($"38: {name} adding an ID card", () => _service.AddIdCardAsync(actor, Card("PRB-IDC-X", free[3]), ct));
        }
        _k.Check(cardHr.Status == IdCardStatuses.Active, "38: a new ID card does not start Active");
        await _k.ExpectAsync<ValidationException>("38: a second active card for the same employee", () => _service.AddIdCardAsync(_k.Hr, Card("PRB-IDC-Y", free[0]), ct));

        // Unique numbers, retired ones included.
        var dupe = await _k.ExpectAsync<ValidationException>("38: a repeated SIM number", () => _service.AddSimAsync(_k.Admin, Sim("prb-sim-1", "9100000003"), ct));
        _k.Check(dupe.FieldErrors.Any(e => e.Field == "simNumber"), "38: the repeated SIM number is not keyed to simNumber");
        var dupeMobile = await _k.ExpectAsync<ValidationException>("38: a repeated mobile number", () => _service.AddSimAsync(_k.Admin, Sim("PRB-SIM-3", "9100000001"), ct));
        _k.Check(dupeMobile.FieldErrors.Any(e => e.Field == "mobileNumber"), "38: the repeated mobile number is not keyed to mobileNumber");
        var dupeTag = await _k.ExpectAsync<ValidationException>("38: a repeated asset tag", () => _service.AddAssetAsync(_k.It, Asset("PRB-AST-1"), ct));
        _k.Check(dupeTag.FieldErrors.Any(e => e.Field == "assetTag"), "38: the repeated asset tag is not keyed to assetTag");

        // Padding and letter case do not make a number new; the serial number is unique too.
        var dupePadded = await _k.ExpectAsync<ValidationException>("38: a padded asset tag", () => _service.AddAssetAsync(_k.It, Asset("  prb-ast-1 "), ct));
        _k.Check(dupePadded.FieldErrors.Any(e => e.Field == "assetTag"), "38: the padded asset tag is not keyed to assetTag");
        var dupeSerial = await _k.ExpectAsync<ValidationException>("38: a repeated serial number", () => _service.AddAssetAsync(_k.It,
            new AssetFieldsBody { AssetTag = "PRB-AST-NEW", AssetType = "Laptop", MakeModel = "Probe Model", SerialNumber = " prb-prb-ast-1" }, ct));
        _k.Check(dupeSerial.FieldErrors.Any(e => e.Field == "serialNumber" && e.Message == "This serial number is already in use."),
            "38: the repeated serial number is not keyed to serialNumber");
        var dupeMobilePadded = await _k.ExpectAsync<ValidationException>("38: a padded mobile number", () => _service.AddSimAsync(_k.Admin, Sim("PRB-SIM-3", " 9100000001 "), ct));
        _k.Check(dupeMobilePadded.FieldErrors.Any(e => e.Field == "mobileNumber"), "38: the padded mobile number is not keyed to mobileNumber");
        var storedTag = await _k.ScalarAsync<string>("SELECT asset_tag FROM assets WHERE id = @Id", new { Id = assetIt.Id });
        _k.Check(storedTag == storedTag.Trim(), "38: a stored asset tag keeps its padding");

        // A save that slips past the checks is refused by the database and still reads as a field message.
        var raced = await _k.ExpectAsync<ValidationException>("38: a duplicate that reaches the database", async () =>
        {
            await SandboxAsync(async (connection, tx) => await _repository.InsertAssetAsync(
                tx, new AssetFields("PRB-AST-RACE", "Laptop", "Probe Model", "PRB-PRB-AST-1"), ct), ct);
        });
        _k.Check(raced.FieldErrors.Any(e => e.Field == "serialNumber"), "38: a database-level duplicate serial number is not keyed to serialNumber");
        var racedSim = await _k.ExpectAsync<ValidationException>("38: a duplicate SIM that reaches the database", async () =>
        {
            await SandboxAsync(async (connection, tx) => await _repository.InsertSimAsync(
                tx, new SimFields("PRB-SIM-RACE", "9100000001", "Jio", "Probe 199", 19900), ct), ct);
        });
        _k.Check(racedSim.FieldErrors.Any(e => e.Field == "mobileNumber"), "38: a database-level duplicate mobile number is not keyed to mobileNumber");

        // A new available record is offered, a retired one is not.
        var offered = await _k.Lookups.Find(MasterLookupKinds.AvailableSim)!.SearchAsync("PRB-SIM-1", 10, ct);
        _k.Check(offered.Any(i => i.Id == simAdmin.Id), "38: a new SIM is not offered by the lookup");

        // Edit: descriptive fields only; holder and status stay.
        var held = (await _service.GetHoldingsAsync(_demo, ct)).Sims[0];
        var before = (await _service.ListSimsAsync(1, 5, null, null, _demo, ct)).Items.Single(s => s.Id == held.Id);
        var edited = await _service.EditSimAsync(_k.Management, held.Id, Sim(before.SimNumber, before.MobileNumber), ct);
        _k.Check(edited.Plan == "Probe 199" && edited.TelecomOperator == "Jio" && edited.MonthlyCost == 199m, "38: the edit did not change the descriptive fields");
        _k.Check(edited.Status == before.Status && edited.HolderEmployeeId == before.HolderEmployeeId && edited.ActivationDate == before.ActivationDate,
            "38: an edit changed the holder, status or activation date");
        await _k.ExpectAsync<ForbiddenException>("38: IT editing a SIM", () => _service.EditSimAsync(_k.It, held.Id, Sim(before.SimNumber, before.MobileNumber), ct));
        await _k.ExpectAsync<NotFoundException>("38: editing an unknown SIM", () => _service.EditSimAsync(_k.Admin, 999999, Sim("PRB-SIM-Z", "9100000099"), ct));
        var editedAsset = await _service.EditAssetAsync(_k.It, assetIt.Id, new AssetFieldsBody { AssetTag = "PRB-AST-1", AssetType = "Laptop", MakeModel = "Probe Model 2", SerialNumber = "PRB-PRB-AST-1" }, ct);
        _k.Check(editedAsset.MakeModel == "Probe Model 2" && editedAsset.Status == AssetStatuses.Available, "38: IT could not edit an asset or the edit changed the status");

        // Retire: soft, refused while held.
        await _service.RetireAsync(_k.Admin, MasterTypes.Sim, simAdmin.Id, ct);
        var retired = await _k.QueryAsync<(int Active, string? Deleted, string? UpdatedBy)>(
            "SELECT is_active, deleted_utc, updated_by FROM sims WHERE id = @Id", new { Id = simAdmin.Id });
        _k.Check(retired.Count == 1 && retired[0].Active == 0 && retired[0].Deleted is not null && retired[0].UpdatedBy == _k.Admin.UserId,
            "38: a retired SIM is not soft-deleted with the actor stamped, or the row is gone");
        _k.Check(!(await _service.ListSimsAsync(1, 100, "PRB-SIM-1", null, null, ct)).Items.Any(s => s.Id == simAdmin.Id), "38: a retired SIM still appears in the list");
        _k.Check(!await _k.Lookups.Find(MasterLookupKinds.AvailableSim)!.ExistsAsync(simAdmin.Id, ct), "38: a retired SIM still validates in the lookup");
        await _k.ExpectAsync<ValidationException>("38: reusing the number of a retired SIM", () => _service.AddSimAsync(_k.Admin, Sim("PRB-SIM-1", "9100000004"), ct));
        await _k.ExpectAsync<NotFoundException>("38: retiring a retired SIM", () => _service.RetireAsync(_k.Admin, MasterTypes.Sim, simAdmin.Id, ct));

        var conflict = await _k.ExpectAsync<ConflictException>("38: retiring a held SIM", () => _service.RetireAsync(_k.Admin, MasterTypes.Sim, held.Id, ct));
        _k.Check(conflict.Code == ErrorCodes.STATE_CONFLICT, "38: retiring a held SIM gave the wrong code");
        _k.Check(await _k.ScalarAsync<long>("SELECT COUNT(*) FROM sims WHERE id = @Id AND is_active = 1 AND deleted_utc IS NULL", new { Id = held.Id }) == 1, "38: the refused retire changed the SIM");
        await _k.ExpectAsync<ConflictException>("38: retiring an active ID card", () => _service.RetireAsync(_k.Hr, MasterTypes.IdCard, cardHr.Id, ct));
        await _k.ExpectAsync<ForbiddenException>("38: IT retiring a SIM", () => _service.RetireAsync(_k.It, MasterTypes.Sim, simManagement.Id, ct));
        await _k.ExpectAsync<ForbiddenException>("38: HR retiring an asset", () => _service.RetireAsync(_k.Hr, MasterTypes.Asset, assetIt.Id, ct));
        await _service.RetireAsync(_k.It, MasterTypes.Asset, assetIt.Id, ct);

        // Audit rows with old and new values.
        var rows = await _k.QueryAsync<ProbeConfigAuditRow>(
            "SELECT request_id AS RequestId, actor_user_id AS ActorUserId, actor_role AS ActorRole, step_key AS StepKey, details_json AS DetailsJson " +
            "FROM audit_events WHERE event_type = 'MasterRecordChanged' ORDER BY id");
        _k.Check(rows.Count > 0 && rows.All(r => r.RequestId is null), "38: master maintenance audit rows are missing or tied to a request");
        JsonNode? Find(string action, string master, long id) =>
            rows.Select(r => Details(r.DetailsJson)).First(d => d!["action"]!.GetValue<string>() == action && d["masterType"]!.GetValue<string>() == master && d["id"]!.GetValue<long>() == id);
        var added = Find("Added", MasterTypes.Sim, simAdmin.Id)!;
        _k.Check(added["old"] is null && added["new"]!["simNumber"]!.GetValue<string>() == "PRB-SIM-1", "38: the add audit row does not hold the new values");
        var edit = Find("Edited", MasterTypes.Sim, held.Id)!;
        _k.Check(edit["old"]!["plan"]!.GetValue<string>() != "Probe 199" && edit["new"]!["plan"]!.GetValue<string>() == "Probe 199", "38: the edit audit row does not hold old and new values");
        var retire = Find("Retired", MasterTypes.Sim, simAdmin.Id)!;
        _k.Check(retire["old"]!["simNumber"]!.GetValue<string>() == "PRB-SIM-1" && retire["new"] is null, "38: the retire audit row does not hold the old values");
        var editRow = rows.Single(r => Details(r.DetailsJson)!["action"]!.GetValue<string>() == "Edited" && Details(r.DetailsJson)!["id"]!.GetValue<long>() == held.Id && Details(r.DetailsJson)!["masterType"]!.GetValue<string>() == MasterTypes.Sim);
        _k.Check(editRow.ActorUserId == _k.Management.UserId && editRow.ActorRole == Roles.Management, "38: the edit audit row has the wrong actor or role");
        var itRow = rows.First(r => r.ActorUserId == _k.It.UserId);
        _k.Check(itRow.ActorRole == Roles.IT, "38: an IT change is not audited with the IT role");
        _k.Pass("38 owner maintenance by role, soft retire, unique numbers and audit rows");
    }

    // ------------------------------------------------------------------ policies

    private async Task PoliciesAsync()
    {
        var authorization = _k.Services.GetRequiredService<IAuthorizationService>();
        var expected = new Dictionary<string, string[]>
        {
            [Policies.SimMasterViewers] = new[] { Roles.Admin, Roles.Management, Roles.SystemAdmin, Roles.IT },
            [Policies.AssetMasterViewers] = new[] { Roles.IT, Roles.Admin, Roles.Management, Roles.SystemAdmin },
            [Policies.IdCardMasterViewers] = new[] { Roles.HR, Roles.Admin, Roles.Management, Roles.SystemAdmin, Roles.IT },
            [Policies.SimMasterEditors] = new[] { Roles.Admin, Roles.Management },
            [Policies.AssetMasterEditors] = new[] { Roles.IT, Roles.Admin, Roles.Management },
            [Policies.IdCardMasterEditors] = new[] { Roles.HR, Roles.Admin, Roles.Management }
        };

        foreach (var (policy, allowed) in expected)
        {
            foreach (var role in Roles.All.Append(Roles.Manager).Distinct())
            {
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Role, role) }, "probe", ClaimTypes.Name, ClaimTypes.Role));
                var result = await authorization.AuthorizeAsync(principal, null, policy);
                _k.Check(result.Succeeded == allowed.Contains(role), $"38: policy {policy} for {role} gave {result.Succeeded}");
            }
        }

        // HR sees ID cards only; IT may edit assets.
        bool Allowed(string role, string policy) => expected[policy].Contains(role);
        _k.Check(Allowed(Roles.HR, Policies.IdCardMasterViewers) && !Allowed(Roles.HR, Policies.SimMasterViewers) && !Allowed(Roles.HR, Policies.AssetMasterViewers),
            "38: HR is not limited to the ID card viewer policy");
        _k.Check(Allowed(Roles.IT, Policies.AssetMasterEditors), "38: IT cannot edit assets");
        _k.Pass("38 master viewer and editor policies for every role");
    }
}
