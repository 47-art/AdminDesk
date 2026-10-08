using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Time;
using Dapper;

namespace AdminDesk.Infrastructure.Masters;

// Applies the master changes of the SIM, laptop, return and ID card modules. It runs on the connection
// and transaction of the step that triggers it, so the step and the master update commit or roll back
// together. Every other module and every other step is ignored.
public sealed class AssetMasterHook : IRequestHook
{
    private readonly IMasterAssetRepository _masters;
    private readonly IAuditRepository _audit;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly TimeProvider _clock;

    public AssetMasterHook(
        IMasterAssetRepository masters, IAuditRepository audit, ICorrelationIdAccessor correlation, TimeProvider clock)
    {
        _masters = masters;
        _audit = audit;
        _correlation = correlation;
        _clock = clock;
    }

    // ---------------------------------------------------------------- creation guard

    public async Task OnCreatingAsync(HookContext context, CancellationToken ct)
    {
        var module = context.Request.ModuleCode;
        if (module is not (MasterModules.SimReturn or MasterModules.AssetReturn))
        {
            return;
        }

        var payload = JsonDocument.Parse(context.Request.PayloadJson).RootElement;
        var requester = context.Request.RequesterEmployeeId;
        var field = module == MasterModules.SimReturn ? MasterModules.SimField : MasterModules.AssetField;
        var itemId = LongOf(payload, field);

        long? holder = null;
        if (itemId is { } id)
        {
            if (module == MasterModules.SimReturn)
            {
                holder = (await _masters.GetSimAsync(context.Transaction, id, ct))?.HolderEmployeeId;
            }
            else
            {
                holder = (await _masters.GetAssetAsync(context.Transaction, id, ct))?.HolderEmployeeId;
            }
        }
        if (holder != requester)
        {
            throw new ValidationException(field, "You can only return an item that is currently allocated to you.");
        }

        if (module == MasterModules.SimReturn && IsTransfer(StringOf(payload, MasterModules.ReasonField)))
        {
            var target = LongOf(payload, MasterModules.TransferToField);
            if (target is null)
            {
                throw new ValidationException(MasterModules.TransferToField, "Choose the employee the SIM moves to.");
            }
            if (target == requester)
            {
                throw new ValidationException(MasterModules.TransferToField, "Choose someone other than yourself.");
            }
        }
    }

    // ------------------------------------------------------------------ step effects

    public async Task OnStepDoneAsync(HookContext context, StepDoneInfo info, CancellationToken ct)
    {
        if (info.Action != RequestAction.Complete)
        {
            return;
        }

        switch (context.Request.ModuleCode, info.StepKey)
        {
            case (MasterModules.Sim, MasterModules.SimMasterUpdateStep):
                await AllocateSimAsync(context, info, ct);
                break;
            case (MasterModules.SimReturn, MasterModules.SimMasterUpdateStep):
                await ReturnSimAsync(context, ct);
                break;
            case (MasterModules.Laptop, MasterModules.AssetMasterUpdateStep):
                await AllocateAssetAsync(context, info, ct);
                break;
            case (MasterModules.AssetReturn, MasterModules.AssetMasterUpdateStep):
                await ReturnAssetAsync(context, info, ct);
                break;
            case (MasterModules.IdCard, MasterModules.IdCardMasterUpdateStep):
                await IssueIdCardAsync(context, info, ct);
                break;
        }
    }

    public Task OnTerminalAsync(HookContext context, TerminalInfo info, CancellationToken ct) => Task.CompletedTask;

    private async Task AllocateSimAsync(HookContext context, StepDoneInfo info, CancellationToken ct)
    {
        var request = context.Request;
        var payload = JsonDocument.Parse(request.PayloadJson).RootElement;
        var allocation = await CapturedAsync(context, info, MasterModules.SimAllocationStep, ct);
        var activation = await CapturedAsync(context, info, MasterModules.TelecomActivationStep, ct);

        var simId = LongOf(allocation, MasterModules.SimField)
            ?? throw new ConflictException("No SIM was chosen for this request.");
        var before = await _masters.GetSimAsync(context.Transaction, simId, ct)
            ?? throw new ConflictException("This SIM is no longer available.");

        DateOnly? activationDate = DateOnly.TryParseExact(
            StringOf(activation, MasterModules.ActivationDateField), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : null;

        if (!await _masters.AllocateSimAsync(context.Transaction, simId, request.RequesterEmployeeId, activationDate, ct))
        {
            throw new ConflictException("This SIM is no longer available.");
        }

        var eventName = StringOf(payload, MasterModules.RequestTypeField) is { Length: > 0 } type ? type : MasterEvents.Allocated;
        await _masters.AppendHistoryAsync(context.Transaction, new HistoryEntry(
            MasterTypes.Sim, simId, eventName, request.RequesterEmployeeId, request.Id, null, null, null), ct);
        await AuditAsync(context, info, MasterTypes.Sim, before.SimNumber, before.Status, SimStatuses.Allocated,
            before.HolderEmployeeId, request.RequesterEmployeeId, ct);
    }

    private async Task ReturnSimAsync(HookContext context, CancellationToken ct)
    {
        var request = context.Request;
        var payload = JsonDocument.Parse(request.PayloadJson).RootElement;
        var simId = LongOf(payload, MasterModules.SimField)
            ?? throw new ConflictException("No SIM was chosen for this request.");
        var before = await _masters.GetSimAsync(context.Transaction, simId, ct)
            ?? throw new ConflictException("This SIM is not held by the requester any more.");
        var requester = request.RequesterEmployeeId;

        if (IsTransfer(StringOf(payload, MasterModules.ReasonField)))
        {
            var target = LongOf(payload, MasterModules.TransferToField)
                ?? throw new ValidationException(MasterModules.TransferToField, "Choose the employee the SIM moves to.");
            if (!await _masters.TransferSimAsync(context.Transaction, simId, requester, target, ct))
            {
                throw new ConflictException("This SIM is not held by the requester any more.");
            }
            var codes = await CodesAsync(context, new[] { requester, target }, ct);
            await _masters.AppendHistoryAsync(context.Transaction, new HistoryEntry(
                MasterTypes.Sim, simId, MasterEvents.Transferred, target, request.Id, null, null,
                $"From {codes.GetValueOrDefault(requester)} to {codes.GetValueOrDefault(target)}"), ct);
            await AuditAsync(context, null, MasterTypes.Sim, before.SimNumber, before.Status, SimStatuses.Allocated,
                before.HolderEmployeeId, target, ct);
            return;
        }

        if (!await _masters.ReleaseSimAsync(context.Transaction, simId, requester, ct))
        {
            throw new ConflictException("This SIM is not held by the requester any more.");
        }
        await _masters.AppendHistoryAsync(context.Transaction, new HistoryEntry(
            MasterTypes.Sim, simId, MasterEvents.Returned, requester, request.Id, null, null,
            StringOf(payload, MasterModules.ReasonField)), ct);
        await AuditAsync(context, null, MasterTypes.Sim, before.SimNumber, before.Status, SimStatuses.Available,
            before.HolderEmployeeId, null, ct);
    }

    private async Task AllocateAssetAsync(HookContext context, StepDoneInfo info, CancellationToken ct)
    {
        var request = context.Request;
        var allocation = await CapturedAsync(context, info, MasterModules.AssetAllocationStep, ct);
        var assetId = LongOf(allocation, MasterModules.AssetField)
            ?? throw new ConflictException("No asset was chosen for this request.");
        var before = await _masters.GetAssetAsync(context.Transaction, assetId, ct)
            ?? throw new ConflictException("This asset is no longer available.");

        if (!await _masters.AllocateAssetAsync(context.Transaction, assetId, request.RequesterEmployeeId, ct))
        {
            throw new ConflictException("This asset is no longer available.");
        }
        await _masters.AppendHistoryAsync(context.Transaction, new HistoryEntry(
            MasterTypes.Asset, assetId, MasterEvents.Allocated, request.RequesterEmployeeId, request.Id, null, null, null), ct);
        await AuditAsync(context, info, MasterTypes.Asset, before.AssetTag, before.Status, AssetStatuses.Allocated,
            before.HolderEmployeeId, request.RequesterEmployeeId, ct);
    }

    private async Task ReturnAssetAsync(HookContext context, StepDoneInfo info, CancellationToken ct)
    {
        var request = context.Request;
        var payload = JsonDocument.Parse(request.PayloadJson).RootElement;
        var assetId = LongOf(payload, MasterModules.AssetField)
            ?? throw new ConflictException("No asset was chosen for this request.");
        var check = await CapturedAsync(context, info, MasterModules.ConditionCheckStep, ct);
        var loss = await CapturedAsync(context, info, MasterModules.DamageLossStep, ct);

        var condition = StringOf(check, MasterModules.ConditionField);
        var newStatus = condition switch
        {
            ItemConditions.Good => AssetStatuses.Available,
            ItemConditions.Damaged => AssetStatuses.Damaged,
            ItemConditions.Lost => AssetStatuses.Lost,
            _ => throw new ValidationException(MasterModules.ConditionField, "The condition of the asset was not recorded.")
        };
        var cost = LongOf(loss, MasterModules.DamageCostField);

        var before = await _masters.GetAssetAsync(context.Transaction, assetId, ct)
            ?? throw new ConflictException("This asset is not held by the requester any more.");
        if (!await _masters.ReleaseAssetAsync(context.Transaction, assetId, request.RequesterEmployeeId, newStatus, condition!, ct))
        {
            throw new ConflictException("This asset is not held by the requester any more.");
        }
        await _masters.AppendHistoryAsync(context.Transaction, new HistoryEntry(
            MasterTypes.Asset, assetId, MasterEvents.Returned, request.RequesterEmployeeId, request.Id, condition, cost, null), ct);
        await AuditAsync(context, info, MasterTypes.Asset, before.AssetTag, before.Status, newStatus,
            before.HolderEmployeeId, null, ct);
    }

    private async Task IssueIdCardAsync(HookContext context, StepDoneInfo info, CancellationToken ct)
    {
        var request = context.Request;
        var payload = JsonDocument.Parse(request.PayloadJson).RootElement;
        var printing = await CapturedAsync(context, info, MasterModules.AdminPrintingStep, ct);

        var number = StringOf(printing, MasterModules.NewCardNumberField)?.Trim();
        if (string.IsNullOrEmpty(number))
        {
            throw new ValidationException(MasterModules.NewCardNumberField, "Enter the new card number.");
        }
        if (await _masters.IsNumberTakenAsync(context.Transaction, MasterNumberKind.CardNumber, number, null, ct))
        {
            throw new ValidationException(MasterModules.NewCardNumberField, "This card number is already in use.");
        }

        var cost = LongOf(printing, MasterModules.ReplacementCostField);
        var requester = request.RequesterEmployeeId;
        var replacement = string.Equals(StringOf(payload, MasterModules.RequestTypeField), MasterModules.RequestTypeReplacement, StringComparison.OrdinalIgnoreCase);

        var replaced = await _masters.ReplaceActiveIdCardsAsync(context.Transaction, requester, ct);
        var today = IndiaTime.Today(_clock);
        var newId = await _masters.InsertIdCardAsync(context.Transaction, number, requester, today, ct);

        foreach (var oldId in replaced)
        {
            await _masters.AppendHistoryAsync(context.Transaction, new HistoryEntry(
                MasterTypes.IdCard, oldId, MasterEvents.Replaced, requester, request.Id, null, null, $"Replaced by card {number}"), ct);
        }

        var notes = new List<string>();
        if (StringOf(payload, MasterModules.ReasonField) is { Length: > 0 } reason)
        {
            notes.Add("Reason: " + reason);
        }
        if (StringOf(payload, MasterModules.OldCardStatusField) is { Length: > 0 } oldStatus)
        {
            notes.Add("Old card: " + oldStatus);
        }
        await _masters.AppendHistoryAsync(context.Transaction, new HistoryEntry(
            MasterTypes.IdCard, newId, replacement ? MasterEvents.Replaced : MasterEvents.Issued, requester, request.Id,
            null, cost, notes.Count == 0 ? null : string.Join("; ", notes)), ct);
        await AuditAsync(context, info, MasterTypes.IdCard, number, replaced.Count > 0 ? IdCardStatuses.Replaced : null, IdCardStatuses.Active,
            requester, requester, ct);
    }

    // ---------------------------------------------------------------- helpers

    private static bool IsTransfer(string? reason) =>
        string.Equals(reason, MasterModules.ReasonTransfer, StringComparison.OrdinalIgnoreCase);

    private static long? LongOf(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static string? StringOf(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // What an earlier step (or the current one) captured; an empty object when it captured nothing.
    private static async Task<JsonElement> CapturedAsync(
        HookContext context, StepDoneInfo info, string stepKey, CancellationToken ct)
    {
        if (info.StepKey == stepKey)
        {
            return JsonSerializer.SerializeToElement(info.Captured);
        }

        var json = await context.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT captured_json FROM request_steps WHERE request_id = @Id AND step_key = @Key",
            new { Id = context.Request.Id, Key = stepKey }, context.Transaction, cancellationToken: ct));
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement.Clone();
    }

    private static async Task<Dictionary<long, string>> CodesAsync(
        HookContext context, IEnumerable<long> employeeIds, CancellationToken ct)
    {
        var rows = await context.Connection.QueryAsync<(long Id, string Code)>(new CommandDefinition(
            "SELECT id AS Id, employee_code AS Code FROM employees WHERE id IN @Ids",
            new { Ids = employeeIds.Distinct().ToArray() }, context.Transaction, cancellationToken: ct));
        return rows.ToDictionary(r => r.Id, r => r.Code);
    }

    private async Task AuditAsync(
        HookContext context, StepDoneInfo? info, string masterType, string item, string? oldStatus, string newStatus,
        long? oldHolder, long? newHolder, CancellationToken ct)
    {
        var step = info?.StepKey ?? context.Request.CurrentStepKey;
        var codes = await CodesAsync(context, new[] { oldHolder, newHolder }.Where(h => h is not null).Select(h => h!.Value), ct);
        var details = new JsonObject
        {
            ["masterType"] = masterType,
            ["item"] = item,
            ["oldStatus"] = oldStatus,
            ["newStatus"] = newStatus,
            ["oldHolder"] = oldHolder is { } o ? codes.GetValueOrDefault(o) : null,
            ["newHolder"] = newHolder is { } n ? codes.GetValueOrDefault(n) : null
        };
        await _audit.AppendAsync(
            context.Transaction,
            new AuditEvent(
                context.Request.Id, AuditEventTypes.MasterUpdated, context.Actor.UserId, context.Actor.Name,
                context.Request.ResponsibleRole, step, null, null, null, details.ToJsonString(),
                _correlation.CorrelationId, _clock.GetUtcNow().UtcDateTime),
            ct);
    }
}
