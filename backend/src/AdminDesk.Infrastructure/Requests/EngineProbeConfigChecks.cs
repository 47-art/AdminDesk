using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Application.Engine;
using AdminDesk.Domain.Definitions;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Requests;

internal sealed class ProbeConfigAuditRow
{
    public long? RequestId { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorRole { get; set; }
    public string? StepKey { get; set; }
    public string? DetailsJson { get; set; }
}

internal sealed class ProbeStoredDefinition
{
    public int Version { get; set; }
    public string DefinitionJson { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
}

// Checks 29 to 34: limits copied onto each request, the config service rules, definition versions
// created by condition edits, the audit rows and the role policies. They work on a stored copy of the
// routing sample (module "configcheck") so the routing module keeps exactly the versions the pinning
// run expects. They run last because they leave the copy's limit and conditions changed.
internal sealed class EngineProbeConfigChecks
{
    private const string Module = "configcheck";
    private const string Gate = "high-value-approval";
    private const string Limit = "amount-limit";

    private readonly ProbeKit _k;
    private readonly IModuleConfigService _config;
    private readonly IDefinitionRepository _definitions;
    private readonly ILimitRepository _limits;

    public EngineProbeConfigChecks(ProbeKit kit)
    {
        _k = kit;
        _config = kit.Services.GetRequiredService<IModuleConfigService>();
        _definitions = kit.Services.GetRequiredService<IDefinitionRepository>();
        _limits = kit.Services.GetRequiredService<ILimitRepository>();
    }

    private static Dictionary<string, JsonElement> Damaged(bool value) => new() { ["damaged"] = ProbeKit.Json(value) };

    private Task<long> CreateAsync(string title, decimal amount) =>
        _k.CreateAsync(_k.Requester, Module, new { title, amount });

    // Review and inspection done; the request is then past the point where the gate step is decided.
    private async Task<long> ThroughInspectionAsync(string title, decimal amount)
    {
        var id = await CreateAsync(title, amount);
        await _k.ActAsync(_k.Admin, id, RequestAction.Approve);
        await _k.ActAsync(_k.Security, id, RequestAction.Complete, null, Damaged(false));
        return id;
    }

    private async Task<string> GateStateAsync(long id) => (await _k.StepAsync(id, Gate)).State;

    private static ConditionInput Rule(string? field, string? op, object? value = null, string? limit = null) =>
        new(field, op, value is null ? null : JsonSerializer.SerializeToElement(value), limit);

    public async Task RunAsync(CancellationToken ct)
    {
        if (await _k.Definitions.GetActiveAsync("routingcheck") is null)
        {
            _k.Logger.LogInformation("Engine probe: routingcheck is not loaded, config checks skipped");
            return;
        }

        await StoreCopyAsync(ct);
        await LimitSnapshotAsync(ct);
        await LimitRulesAsync(ct);
        await ConditionRulesAsync(ct);
        await VersionsAsync(ct);
        await GroupConditionAsync(ct);
        await AuditAndPoliciesAsync();
    }

    // The copy is stored like any other definition version, with its limits.
    private async Task StoreCopyAsync(CancellationToken ct)
    {
        _k.Check(await _definitions.MaxVersionAsync(Module, ct) is null, "29: the config check module already exists; use a fresh data folder");
        var source = await _k.Definitions.GetActiveAsync("routingcheck") ?? throw _k.Fail("29: routingcheck is not active");
        var row = await _definitions.GetByIdAsync(source.Id, ct) ?? throw _k.Fail("29: routingcheck row is missing");

        var root = JsonNode.Parse(row.DefinitionJson)!.AsObject();
        root["code"] = Module;
        root["name"] = "Config check";
        root["prefix"] = "CFG";
        root["version"] = 1;
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var copy = DefinitionJson.Parse(json);

        var unitOfWork = _k.Services.GetRequiredService<IUnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            await _definitions.InsertAsync(tx, new DefinitionRow(
                0, Module, 1, copy.Name, copy.Category, copy.Prefix, json, DefinitionHash.Of(Encoding.UTF8.GetBytes(json))), ct);
            foreach (var limit in copy.Limits)
            {
                await _limits.InsertIfMissingAsync(tx, new LimitRow(Module, limit.StepKey, limit.LimitKey, limit.ValueMinor, limit.Unit, true), ct);
            }
            return 0;
        }, ct);
        _k.Services.GetRequiredService<IDefinitionProvider>().Refresh();
        _k.Check((await _k.Definitions.GetActiveAsync(Module))?.Definition.Version == 1, "29: the config check module is not active");
    }

    // 29: a request keeps the limits it started with.
    private async Task LimitSnapshotAsync(CancellationToken ct)
    {
        var before = await ThroughInspectionAsync("Snapshot before", 20000m);
        _k.Check(await GateStateAsync(before) == "NotRequired", "29: 20000 should be under the starting limit");

        var early = await CreateAsync("Snapshot early", 20000m);
        await _config.UpdateLimitAsync(_k.Management, Module, Gate, Limit, 1_000_000, ct);
        var stored = await _limits.GetAsync(Module, Gate, Limit, ct);
        _k.Check(stored is { ValueMinor: 1_000_000, IsSample: false }, "29: the limit was not updated");

        var after = await ThroughInspectionAsync("Snapshot after", 20000m);
        _k.Check(await GateStateAsync(after) == "Pending", "29: a request created after the change did not use the new limit");

        // Created before the change, decided after it: still the old limit.
        await _k.ActAsync(_k.Admin, early, RequestAction.Approve);
        await _k.ActAsync(_k.Security, early, RequestAction.Complete, null, Damaged(false));
        _k.Check(await GateStateAsync(early) == "NotRequired", "29: a request in flight was rerouted by the limit change");

        var snapshots = await _k.QueryAsync<string>(
            "SELECT limits_json FROM requests WHERE id IN (@A, @B) ORDER BY id", new { A = early, B = after });
        _k.Check(snapshots.Count == 2 && snapshots[0].Contains("5000000") && snapshots[1].Contains("1000000"),
            "29: the stored limits do not match the limits at creation");

        // A request from before snapshots existed follows the live limit.
        var legacy = await CreateAsync("Snapshot legacy", 20000m);
        await _k.ExecuteRawAsync($"UPDATE requests SET limits_json = NULL WHERE id = {legacy}");
        await _k.ActAsync(_k.Admin, legacy, RequestAction.Approve);
        await _k.ActAsync(_k.Security, legacy, RequestAction.Complete, null, Damaged(false));
        _k.Check(await GateStateAsync(legacy) == "Pending", "29: a request without stored limits did not use the live limit");
        _k.Pass("29 limits snapshot per request");
    }

    // 30: limit updates are checked.
    private async Task LimitRulesAsync(CancellationToken ct)
    {
        var negative = await _k.ExpectAsync<ValidationException>("30: negative limit",
            () => _config.UpdateLimitAsync(_k.Management, Module, Gate, Limit, -1, ct));
        _k.Check(negative.FieldErrors.Any(e => e.Field == "valueMinor"), "30: negative limit not reported on valueMinor");
        await _k.ExpectAsync<NotFoundException>("30: unknown limit",
            () => _config.UpdateLimitAsync(_k.Management, Module, Gate, "nope", 10, ct));
        await _k.ExpectAsync<NotFoundException>("30: unknown module",
            () => _config.UpdateLimitAsync(_k.Management, "no-such-module", Gate, Limit, 10, ct));
        await _k.ExpectAsync<ForbiddenException>("30: requester changing a limit",
            () => _config.UpdateLimitAsync(_k.Requester, Module, Gate, Limit, 10, ct));
        var stored = await _limits.GetAsync(Module, Gate, Limit, ct);
        _k.Check(stored?.ValueMinor == 1_000_000, "30: a refused update changed the limit");
        _k.Pass("30 limit update rules");
    }

    private async Task ExpectRejectedAsync(string what, string field, ConditionInput condition, int baseVersion, CancellationToken ct, string step = Gate)
    {
        var failure = await _k.ExpectAsync<ValidationException>(what,
            () => _config.SaveConditionAsync(_k.Management, Module, step, baseVersion, condition, ct));
        _k.Check(failure.FieldErrors.Any(e => e.Field == field),
            $"{what}: no error keyed to '{field}' (got {string.Join(", ", failure.FieldErrors.Select(e => e.Field))})");
    }

    // 31: condition edits are checked against the fields, operators and limits on offer.
    private async Task ConditionRulesAsync(CancellationToken ct)
    {
        var version = (await _k.Definitions.GetActiveAsync(Module))!.Definition.Version;
        await ExpectRejectedAsync("31: unknown field", "condition.field", Rule("nope", "gt", 1), version, ct);
        await ExpectRejectedAsync("31: field of a later step", "condition.field", Rule("extra-check.repairCost", "gt", 1), version, ct, "damage-assessment");
        await ExpectRejectedAsync("31: unknown operator", "condition.op", Rule("amount", "banana", 1), version, ct);
        await ExpectRejectedAsync("31: list operator on money", "condition.op", Rule("amount", "in", new[] { 1 }), version, ct);
        await ExpectRejectedAsync("31: ordering on yes/no", "condition.op", Rule("inspection.damaged", "gt", true), version, ct);
        await ExpectRejectedAsync("31: value and limit", "condition.value", Rule("amount", "gt", 100, Limit), version, ct);
        await ExpectRejectedAsync("31: neither value nor limit", "condition.value", Rule("amount", "gt"), version, ct);
        await ExpectRejectedAsync("31: unknown limit key", "condition.limit", Rule("amount", "gt", null, "nope"), version, ct);
        await ExpectRejectedAsync("31: limit of another step", "condition.limit", Rule("inspection.damaged", "eq", null, Limit), version, ct, "damage-assessment");
        await ExpectRejectedAsync("31: text for money", "condition.value", Rule("amount", "gt", "lots"), version, ct);
        await ExpectRejectedAsync("31: paise fraction", "condition.value", Rule("amount", "gt", 10.005m), version, ct);
        await ExpectRejectedAsync("31: value on isEmpty", "condition.value", Rule("amount", "isEmpty", 1), version, ct);
        _k.Check((await _k.Definitions.GetActiveAsync(Module))!.Definition.Version == version, "31: a refused condition published a version");
        _k.Pass("31 condition rules");
    }

    private async Task<List<ProbeStoredDefinition>> StoredAsync() =>
        await _k.QueryAsync<ProbeStoredDefinition>(
            "SELECT version, definition_json AS DefinitionJson, content_hash AS ContentHash FROM module_definitions WHERE code = @Code ORDER BY version",
            new { Code = Module });

    private static JsonNode? WithoutGate(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root.Remove("version");
        FindGate(root).Remove("condition");
        return root;
    }

    private static JsonObject FindGate(JsonObject root) =>
        root["steps"]!.AsArray().OfType<JsonObject>().Single(s => s["key"]!.GetValue<string>() == Gate);

    // 32: each saved condition is a new version; requests already running keep their own version.
    private async Task VersionsAsync(CancellationToken ct)
    {
        var inFlight = await CreateAsync("Version in flight", 5000m);
        await _k.ActAsync(_k.Admin, inFlight, RequestAction.Approve);
        _k.Check((await _k.RequestAsync(inFlight)).DefinitionVersion == 1, "32: the in-flight request is not on version 1");

        // Clear the condition: version 2.
        var v2 = await _config.SaveConditionAsync(_k.Management, Module, Gate, 1, null, ct);
        _k.Check(v2 == 2 && (await _k.Definitions.GetActiveAsync(Module))!.Definition.Version == 2, "32: clearing did not publish version 2");
        var cleared = await ThroughInspectionAsync("Cleared", 20000m);
        _k.Check((await _k.RequestAsync(cleared)).DefinitionVersion == 2 && await GateStateAsync(cleared) == "Pending",
            "32: a new request did not use the cleared condition (a step without a condition is always required)");

        // Clearing again changes nothing.
        _k.Check(await _config.SaveConditionAsync(_k.SysAdmin, Module, Gate, 2, null, ct) == 2, "32: clearing an empty condition published a version");

        // A fixed value: version 3.
        var v3 = await _config.SaveConditionAsync(_k.SysAdmin, Module, Gate, 2, Rule("amount", "gt", 25000), ct);
        _k.Check(v3 == 3, "32: setting a rule did not publish version 3");
        var low = await ThroughInspectionAsync("Value low", 20000m);
        var high = await ThroughInspectionAsync("Value high", 30000m);
        _k.Check(await GateStateAsync(low) == "NotRequired" && await GateStateAsync(high) == "Pending", "32: the fixed value rule routed wrongly");

        // Back to a limit: version 4.
        var v4 = await _config.SaveConditionAsync(_k.Management, Module, Gate, 3, Rule("amount", "gte", null, Limit), ct);
        _k.Check(v4 == 4, "32: setting a limit rule did not publish version 4");
        var limited = await ThroughInspectionAsync("Limit rule", 20000m);
        _k.Check((await _k.RequestAsync(limited)).DefinitionVersion == 4 && await GateStateAsync(limited) == "Pending",
            "32: the limit rule routed wrongly");

        // The request created on version 1 still decides by version 1 and the limit it started with
        // (5000 is below 10000), although version 2 made the extra approval unconditional.
        await _k.ActAsync(_k.Security, inFlight, RequestAction.Complete, null, Damaged(false));
        var flight = await _k.RequestAsync(inFlight);
        _k.Check(flight.DefinitionVersion == 1 && await GateStateAsync(inFlight) == "NotRequired",
            "32: the in-flight request was rerouted by a newer version");

        var stored = await StoredAsync();
        _k.Check(stored.Select(s => s.Version).SequenceEqual(new[] { 1, 2, 3, 4 }), "32: stored versions are not 1 to 4");
        _k.Check(stored.All(s => s.ContentHash == DefinitionHash.Of(Encoding.UTF8.GetBytes(s.DefinitionJson))) &&
                 stored.Select(s => s.ContentHash).Distinct().Count() == 4,
            "32: content hashes are missing or repeated");
        _k.Check(JsonNode.DeepEquals(WithoutGate(stored[0].DefinitionJson), WithoutGate(stored[1].DefinitionJson)),
            "32: clearing a condition changed more than that step");
        _k.Check(JsonNode.DeepEquals(WithoutGate(stored[2].DefinitionJson), WithoutGate(stored[3].DefinitionJson)),
            "32: changing a rule changed more than that step");
        _k.Check(FindGate(JsonNode.Parse(stored[0].DefinitionJson)!.AsObject())["condition"] is not null, "32: version 1 lost its condition");

        // A stale base version is refused and publishes nothing.
        var conflict = await _k.ExpectAsync<ConflictException>("32: stale base version",
            () => _config.SaveConditionAsync(_k.Management, Module, Gate, 1, null, ct));
        _k.Check(conflict.Code == ErrorCodes.STATE_CONFLICT, "32: stale base version gave the wrong code");
        _k.Check((await StoredAsync()).Count == 4, "32: a stale save published a version");

        // The read model shows what is stored.
        var all = await _config.GetAllAsync(ct);
        _k.Check(new[] { "stationery", "courier", Module }.All(code => all.Any(m => m.Code == code)), "32: the config list misses a module");
        var module = all.Single(m => m.Code == Module);
        var gate = module.Steps.Single(s => s.Key == Gate);
        _k.Check(module.Version == 4 && gate.Condition is { Field: "amount", Op: "gte", Limit: Limit } && gate.ConditionEditable,
            "32: the config read does not show the stored condition");
        _k.Check(gate.LimitKeys.SequenceEqual(new[] { Limit }), "32: the gate step does not list its limit");
        _k.Check(module.Limits.Any(l => l.StepKey == Gate && l.LimitKey == Limit && l.ValueMinor == 1_000_000), "32: the config read misses the limit");
        var damage = module.Steps.Single(s => s.Key == "damage-assessment");
        var keys = damage.ConditionFields.Select(f => f.Key).ToList();
        _k.Check(keys.Contains("amount") && keys.Contains("inspection.damaged") && !keys.Contains("extra-check.repairCost"),
            "32: the condition choices are not limited to earlier fields");
        var money = damage.ConditionFields.Single(f => f.Key == "amount");
        _k.Check(money.Type == "Money" && money.AllowsLimit && money.Operators.Contains("gt"), "32: the money choice is wrong");
        _k.Pass("32 condition versions and read model");
    }

    // 33: a condition that is an any / all group is shown as not editable and replaced by a single rule.
    private async Task GroupConditionAsync(CancellationToken ct)
    {
        var latest = (await StoredAsync()).Last();
        var root = JsonNode.Parse(latest.DefinitionJson)!.AsObject();
        root["version"] = latest.Version + 1;
        FindGate(root)["condition"] = JsonNode.Parse(
            "{\"any\":[{\"field\":\"amount\",\"op\":\"gt\",\"limit\":\"amount-limit\"},{\"field\":\"inspection.damaged\",\"op\":\"eq\",\"value\":true}]}");
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var unitOfWork = _k.Services.GetRequiredService<IUnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            await _definitions.InsertAsync(tx, new DefinitionRow(
                0, Module, latest.Version + 1, "Config check", "Visitors and others", "CFG", json, DefinitionHash.Of(Encoding.UTF8.GetBytes(json))), ct);
            return 0;
        }, ct);
        _k.Services.GetRequiredService<IDefinitionProvider>().Refresh();

        var gate = (await _config.GetAsync(Module, ct)).Steps.Single(s => s.Key == Gate);
        _k.Check(!gate.ConditionEditable && gate.Condition?.Any is { Count: 2 } && !string.IsNullOrEmpty(gate.ConditionSummary),
            "33: a group condition is not reported as read-only");

        var version = latest.Version + 1;
        var saved = await _config.SaveConditionAsync(_k.Management, Module, Gate, version, Rule("amount", "gt", null, Limit), ct);
        var after = (await _config.GetAsync(Module, ct)).Steps.Single(s => s.Key == Gate);
        _k.Check(saved == version + 1 && after.ConditionEditable && after.Condition is { Field: "amount", Any: null },
            "33: a single rule did not replace the group");
        _k.Pass("33 group condition replaced by a rule");
    }

    // 34: audit rows and role policies.
    private async Task AuditAndPoliciesAsync()
    {
        var limitRows = await _k.QueryAsync<ProbeConfigAuditRow>(
            "SELECT request_id AS RequestId, actor_user_id AS ActorUserId, actor_role AS ActorRole, step_key AS StepKey, details_json AS DetailsJson " +
            "FROM audit_events WHERE event_type = 'LimitChanged' AND json_extract(details_json, '$.module') = @Module ORDER BY id",
            new { Module });
        _k.Check(limitRows.Count == 1, $"34: expected one limit audit row, found {limitRows.Count}");
        var limit = limitRows[0];
        var limitDetails = JsonNode.Parse(limit.DetailsJson!)!;
        _k.Check(limit.RequestId is null && limit.ActorUserId == _k.Management.UserId && limit.ActorRole == Roles.Management && limit.StepKey == Gate,
            "34: the limit audit row has the wrong request, actor or step");
        _k.Check(limitDetails["old"]!.GetValue<long>() == 5_000_000 && limitDetails["new"]!.GetValue<long>() == 1_000_000 &&
                 limitDetails["limit"]!.GetValue<string>() == Limit,
            "34: the limit audit row does not hold the old and new values");

        var conditionRows = await _k.QueryAsync<ProbeConfigAuditRow>(
            "SELECT request_id AS RequestId, actor_user_id AS ActorUserId, actor_role AS ActorRole, step_key AS StepKey, details_json AS DetailsJson " +
            "FROM audit_events WHERE event_type = 'ConditionChanged' AND json_extract(details_json, '$.module') = @Module ORDER BY id",
            new { Module });
        _k.Check(conditionRows.Count == 4, $"34: expected four condition audit rows, found {conditionRows.Count}");
        _k.Check(conditionRows.All(r => r.RequestId is null && r.StepKey is not null), "34: a condition audit row is tied to a request or has no step");
        var first = JsonNode.Parse(conditionRows[0].DetailsJson!)!;
        _k.Check(first["old"] is JsonObject && first["new"] is null && first["version"]!.GetValue<int>() == 2, "34: clearing is not audited as old rule to none");
        var second = JsonNode.Parse(conditionRows[1].DetailsJson!)!;
        _k.Check(conditionRows[1].ActorRole == Roles.SystemAdmin && conditionRows[1].ActorUserId == _k.SysAdmin.UserId &&
                 second["old"] is null && second["new"]!["field"]!.GetValue<string>() == "amount" && second["version"]!.GetValue<int>() == 3,
            "34: setting a rule is not audited with actor and values");

        var authorization = _k.Services.GetRequiredService<IAuthorizationService>();
        foreach (var policy in new[] { Policies.LimitEditors, Policies.ConfigViewers })
        {
            foreach (var role in Roles.All)
            {
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Role, role) }, "probe", ClaimTypes.Name, ClaimTypes.Role));
                var result = await authorization.AuthorizeAsync(principal, null, policy);
                var expected = role is Roles.Management or Roles.SystemAdmin;
                _k.Check(result.Succeeded == expected, $"34: policy {policy} for {role} gave {result.Succeeded}, expected {expected}");
            }
        }
        _k.Pass("34 audit rows and role policies");
    }
}
