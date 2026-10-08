using System.Text.Json.Nodes;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Engine;
using AdminDesk.SharedKernel.Actors;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Money;
using AdminDesk.SharedKernel.Responses;

namespace AdminDesk.Application.Masters;

public interface IMasterAssetService
{
    Task<PagedResult<SimDto>> ListSimsAsync(int page, int pageSize, string? search, string? status, long? holder, CancellationToken ct);

    Task<PagedResult<AssetDto>> ListAssetsAsync(int page, int pageSize, string? search, string? status, long? holder, CancellationToken ct);

    Task<PagedResult<IdCardDto>> ListIdCardsAsync(int page, int pageSize, string? search, string? status, long? holder, CancellationToken ct);

    Task<IReadOnlyList<MasterHistoryDto>> GetHistoryAsync(string masterType, long id, CancellationToken ct);

    Task<HoldingsDto> GetHoldingsAsync(long employeeId, CancellationToken ct);

    Task<SimDto> AddSimAsync(ActorContext actor, SimFieldsBody body, CancellationToken ct);

    Task<AssetDto> AddAssetAsync(ActorContext actor, AssetFieldsBody body, CancellationToken ct);

    Task<IdCardDto> AddIdCardAsync(ActorContext actor, IdCardAddBody body, CancellationToken ct);

    Task<SimDto> EditSimAsync(ActorContext actor, long id, SimFieldsBody body, CancellationToken ct);

    Task<AssetDto> EditAssetAsync(ActorContext actor, long id, AssetFieldsBody body, CancellationToken ct);

    Task<IdCardDto> EditIdCardAsync(ActorContext actor, long id, IdCardEditBody body, CancellationToken ct);

    Task RetireAsync(ActorContext actor, string masterType, long id, CancellationToken ct);
}

// Reads the masters and lets their owning roles add, edit and retire records. Holder, status and
// condition are never taken from a caller: they change only when a request step completes.
public sealed class MasterAssetService : IMasterAssetService
{
    private readonly IMasterAssetRepository _repository;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditRepository _audit;
    private readonly IActorAccessor _actorAccessor;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly TimeProvider _clock;

    public MasterAssetService(
        IMasterAssetRepository repository,
        IEmployeeRepository employees,
        IUnitOfWork unitOfWork,
        IAuditRepository audit,
        IActorAccessor actorAccessor,
        ICorrelationIdAccessor correlation,
        TimeProvider clock)
    {
        _repository = repository;
        _employees = employees;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _actorAccessor = actorAccessor;
        _correlation = correlation;
        _clock = clock;
    }

    // ------------------------------------------------------------------ read

    public Task<PagedResult<SimDto>> ListSimsAsync(
        int page, int pageSize, string? search, string? status, long? holder, CancellationToken ct) =>
        _repository.ListSimsAsync(Query(page, pageSize, search, status, holder, SimStatuses.All), ct);

    public Task<PagedResult<AssetDto>> ListAssetsAsync(
        int page, int pageSize, string? search, string? status, long? holder, CancellationToken ct) =>
        _repository.ListAssetsAsync(Query(page, pageSize, search, status, holder, AssetStatuses.All), ct);

    public Task<PagedResult<IdCardDto>> ListIdCardsAsync(
        int page, int pageSize, string? search, string? status, long? holder, CancellationToken ct) =>
        _repository.ListIdCardsAsync(Query(page, pageSize, search, status, holder, IdCardStatuses.All), ct);

    public async Task<IReadOnlyList<MasterHistoryDto>> GetHistoryAsync(string masterType, long id, CancellationToken ct)
    {
        if (!MasterTypes.All.Contains(masterType) || !await _repository.ExistsAsync(masterType, id, ct))
        {
            throw new NotFoundException("This record was not found.");
        }
        return await _repository.GetHistoryAsync(masterType, id, ct);
    }

    public Task<HoldingsDto> GetHoldingsAsync(long employeeId, CancellationToken ct) =>
        _repository.ListHeldByAsync(employeeId, ct);

    private static MasterListQuery Query(
        int page, int pageSize, string? search, string? status, long? holder, string[] allowedStatuses)
    {
        var text = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var chosen = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (chosen is not null)
        {
            chosen = allowedStatuses.FirstOrDefault(s => string.Equals(s, chosen, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException("status", "Choose one of the listed statuses.");
        }
        return new MasterListQuery(
            Math.Max(page, 1), Math.Clamp(pageSize, 1, MasterService.MaxPageSize), text, chosen, holder);
    }

    // ----------------------------------------------------------------- add

    public async Task<SimDto> AddSimAsync(ActorContext actor, SimFieldsBody body, CancellationToken ct)
    {
        var editorRole = RequireEditor(actor, MasterTypes.Sim);
        var fields = ToSimFields(body);
        _actorAccessor.Use(actor.UserId);
        return await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            await CheckSimNumbersAsync(tx, fields, null, ct);
            var id = await _repository.InsertSimAsync(tx, fields, ct);
            var created = await _repository.GetSimAsync(tx, id, ct) ?? throw new InvalidOperationException("The SIM was not saved.");
            await _repository.AppendHistoryAsync(tx, new HistoryEntry(MasterTypes.Sim, id, MasterEvents.Added, null, null, null, null, null), ct);
            await AuditAsync(tx, actor, editorRole, "Added", MasterTypes.Sim, id, created.SimNumber, null, SimValues(created), ct);
            return created;
        }, ct);
    }

    public async Task<AssetDto> AddAssetAsync(ActorContext actor, AssetFieldsBody body, CancellationToken ct)
    {
        var editorRole = RequireEditor(actor, MasterTypes.Asset);
        var fields = ToAssetFields(body);
        _actorAccessor.Use(actor.UserId);
        return await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            await CheckAssetNumbersAsync(tx, fields, null, ct);
            var id = await _repository.InsertAssetAsync(tx, fields, ct);
            var created = await _repository.GetAssetAsync(tx, id, ct) ?? throw new InvalidOperationException("The asset was not saved.");
            await _repository.AppendHistoryAsync(tx, new HistoryEntry(MasterTypes.Asset, id, MasterEvents.Added, null, null, null, null, null), ct);
            await AuditAsync(tx, actor, editorRole, "Added", MasterTypes.Asset, id, created.AssetTag, null, AssetValues(created), ct);
            return created;
        }, ct);
    }

    public async Task<IdCardDto> AddIdCardAsync(ActorContext actor, IdCardAddBody body, CancellationToken ct)
    {
        var editorRole = RequireEditor(actor, MasterTypes.IdCard);
        var number = Clean(body.CardNumber, "cardNumber", "Enter the card number.");
        var employeeId = body.EmployeeId ?? throw new ValidationException("employeeId", "Choose the employee.");
        var issued = body.IssuedDate ?? throw new ValidationException("issuedDate", "Enter the issued date.");
        if (await _employees.GetByIdAsync(employeeId, ct) is null)
        {
            throw new ValidationException("employeeId", "Choose an employee from the list.");
        }

        _actorAccessor.Use(actor.UserId);
        return await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            if (await _repository.IsNumberTakenAsync(tx, MasterNumberKind.CardNumber, number, null, ct))
            {
                throw new ValidationException("cardNumber", "This card number is already in use.");
            }
            if (await _repository.HasActiveIdCardAsync(tx, employeeId, ct))
            {
                throw new ValidationException("employeeId", "This employee already has an active card.");
            }
            var id = await _repository.InsertIdCardAsync(tx, number, employeeId, issued, ct);
            var created = await _repository.GetIdCardAsync(tx, id, ct) ?? throw new InvalidOperationException("The card was not saved.");
            await _repository.AppendHistoryAsync(
                tx, new HistoryEntry(MasterTypes.IdCard, id, MasterEvents.Added, employeeId, null, null, null, null), ct);
            await AuditAsync(tx, actor, editorRole, "Added", MasterTypes.IdCard, id, created.CardNumber, null, IdCardValues(created), ct);
            return created;
        }, ct);
    }

    // ---------------------------------------------------------------- edit

    public async Task<SimDto> EditSimAsync(ActorContext actor, long id, SimFieldsBody body, CancellationToken ct)
    {
        var editorRole = RequireEditor(actor, MasterTypes.Sim);
        var fields = ToSimFields(body);
        _actorAccessor.Use(actor.UserId);
        return await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            var before = await _repository.GetSimAsync(tx, id, ct) ?? throw NotFound();
            await CheckSimNumbersAsync(tx, fields, id, ct);
            await _repository.UpdateSimAsync(tx, id, fields, ct);
            var after = await _repository.GetSimAsync(tx, id, ct) ?? throw NotFound();
            await _repository.AppendHistoryAsync(tx, new HistoryEntry(MasterTypes.Sim, id, MasterEvents.Edited, null, null, null, null, null), ct);
            await AuditAsync(tx, actor, editorRole, "Edited", MasterTypes.Sim, id, after.SimNumber, SimValues(before), SimValues(after), ct);
            return after;
        }, ct);
    }

    public async Task<AssetDto> EditAssetAsync(ActorContext actor, long id, AssetFieldsBody body, CancellationToken ct)
    {
        var editorRole = RequireEditor(actor, MasterTypes.Asset);
        var fields = ToAssetFields(body);
        _actorAccessor.Use(actor.UserId);
        return await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            var before = await _repository.GetAssetAsync(tx, id, ct) ?? throw NotFound();
            await CheckAssetNumbersAsync(tx, fields, id, ct);
            await _repository.UpdateAssetAsync(tx, id, fields, ct);
            var after = await _repository.GetAssetAsync(tx, id, ct) ?? throw NotFound();
            await _repository.AppendHistoryAsync(tx, new HistoryEntry(MasterTypes.Asset, id, MasterEvents.Edited, null, null, null, null, null), ct);
            await AuditAsync(tx, actor, editorRole, "Edited", MasterTypes.Asset, id, after.AssetTag, AssetValues(before), AssetValues(after), ct);
            return after;
        }, ct);
    }

    public async Task<IdCardDto> EditIdCardAsync(ActorContext actor, long id, IdCardEditBody body, CancellationToken ct)
    {
        var editorRole = RequireEditor(actor, MasterTypes.IdCard);
        var number = Clean(body.CardNumber, "cardNumber", "Enter the card number.");
        var issued = body.IssuedDate ?? throw new ValidationException("issuedDate", "Enter the issued date.");
        _actorAccessor.Use(actor.UserId);
        return await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            var before = await _repository.GetIdCardAsync(tx, id, ct) ?? throw NotFound();
            if (await _repository.IsNumberTakenAsync(tx, MasterNumberKind.CardNumber, number, id, ct))
            {
                throw new ValidationException("cardNumber", "This card number is already in use.");
            }
            await _repository.UpdateIdCardAsync(tx, id, new IdCardFields(number, issued), ct);
            var after = await _repository.GetIdCardAsync(tx, id, ct) ?? throw NotFound();
            await _repository.AppendHistoryAsync(
                tx, new HistoryEntry(MasterTypes.IdCard, id, MasterEvents.Edited, after.EmployeeId, null, null, null, null), ct);
            await AuditAsync(tx, actor, editorRole, "Edited", MasterTypes.IdCard, id, after.CardNumber, IdCardValues(before), IdCardValues(after), ct);
            return after;
        }, ct);
    }

    // --------------------------------------------------------------- retire

    public async Task RetireAsync(ActorContext actor, string masterType, long id, CancellationToken ct)
    {
        if (!MasterTypes.All.Contains(masterType))
        {
            throw NotFound();
        }
        var editorRole = RequireEditor(actor, masterType);
        _actorAccessor.Use(actor.UserId);
        await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            string label;
            JsonObject before;
            bool held;
            switch (masterType)
            {
                case MasterTypes.Sim:
                {
                    var sim = await _repository.GetSimAsync(tx, id, ct) ?? throw NotFound();
                    (label, before, held) = (sim.SimNumber, SimValues(sim), sim.HolderEmployeeId is not null);
                    break;
                }
                case MasterTypes.Asset:
                {
                    var asset = await _repository.GetAssetAsync(tx, id, ct) ?? throw NotFound();
                    (label, before, held) = (asset.AssetTag, AssetValues(asset), asset.HolderEmployeeId is not null);
                    break;
                }
                default:
                {
                    var card = await _repository.GetIdCardAsync(tx, id, ct) ?? throw NotFound();
                    (label, before, held) = (card.CardNumber, IdCardValues(card), card.Status == IdCardStatuses.Active);
                    break;
                }
            }

            if (held)
            {
                throw new ConflictException("This record is held by someone. Return it first, then retire it.");
            }
            if (!await _repository.RetireAsync(tx, masterType, id, ct))
            {
                throw new ConflictException("This record is held by someone. Return it first, then retire it.");
            }
            await _repository.AppendHistoryAsync(tx, new HistoryEntry(masterType, id, MasterEvents.Retired, null, null, null, null, null), ct);
            await AuditAsync(tx, actor, editorRole, "Retired", masterType, id, label, before, null, ct);
            return 0;
        }, ct);
    }

    // -------------------------------------------------------------- helpers

    private static NotFoundException NotFound() => new("This record was not found.");

    private static string[] EditorsOf(string masterType) => masterType switch
    {
        MasterTypes.Sim => Roles.SimMasterEditors,
        MasterTypes.Asset => Roles.AssetMasterEditors,
        _ => Roles.IdCardMasterEditors
    };

    // Returns the role that entitles the actor, for the audit row.
    private static string RequireEditor(ActorContext actor, string masterType)
    {
        var role = EditorsOf(masterType).FirstOrDefault(actor.Roles.Contains);
        return role ?? throw new ForbiddenException("You are not allowed to change these records.");
    }

    private static string Clean(string? value, string field, string message)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? throw new ValidationException(field, message) : text;
    }

    private static SimFields ToSimFields(SimFieldsBody body)
    {
        var cost = body.MonthlyCost ?? throw new ValidationException("monthlyCost", "Enter the monthly cost.");
        if (cost < 0 || decimal.Round(cost, 2) != cost)
        {
            throw new ValidationException("monthlyCost", "Enter an amount of zero or more with at most two decimal places.");
        }
        return new SimFields(
            Clean(body.SimNumber, "simNumber", "Enter the SIM number."),
            Clean(body.MobileNumber, "mobileNumber", "Enter the mobile number."),
            Clean(body.TelecomOperator, "telecomOperator", "Enter the telecom operator."),
            Clean(body.Plan, "plan", "Enter the plan."),
            MoneyConverter.ToMinor(cost));
    }

    private static AssetFields ToAssetFields(AssetFieldsBody body) => new(
        Clean(body.AssetTag, "assetTag", "Enter the asset tag."),
        Clean(body.AssetType, "assetType", "Enter the asset type."),
        Clean(body.MakeModel, "makeModel", "Enter the make and model."),
        Clean(body.SerialNumber, "serialNumber", "Enter the serial number."));

    private async Task CheckSimNumbersAsync(System.Data.Common.DbTransaction tx, SimFields fields, long? exceptId, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (await _repository.IsNumberTakenAsync(tx, MasterNumberKind.SimNumber, fields.SimNumber, exceptId, ct))
        {
            errors.Add(new FieldError { Field = "simNumber", Message = "This SIM number is already in use." });
        }
        if (await _repository.IsNumberTakenAsync(tx, MasterNumberKind.MobileNumber, fields.MobileNumber, exceptId, ct))
        {
            errors.Add(new FieldError { Field = "mobileNumber", Message = "This mobile number is already in use." });
        }
        if (errors.Count > 0)
        {
            throw new ValidationException(errors, errors[0].Message);
        }
    }

    private async Task CheckAssetNumbersAsync(System.Data.Common.DbTransaction tx, AssetFields fields, long? exceptId, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (await _repository.IsNumberTakenAsync(tx, MasterNumberKind.AssetTag, fields.AssetTag, exceptId, ct))
        {
            errors.Add(new FieldError { Field = "assetTag", Message = "This asset tag is already in use." });
        }
        if (await _repository.IsNumberTakenAsync(tx, MasterNumberKind.SerialNumber, fields.SerialNumber, exceptId, ct))
        {
            errors.Add(new FieldError { Field = "serialNumber", Message = "This serial number is already in use." });
        }
        if (errors.Count > 0)
        {
            throw new ValidationException(errors, errors[0].Message);
        }
    }

    private static JsonObject SimValues(SimDto s) => new()
    {
        ["simNumber"] = s.SimNumber,
        ["mobileNumber"] = s.MobileNumber,
        ["telecomOperator"] = s.TelecomOperator,
        ["plan"] = s.Plan,
        ["monthlyCost"] = s.MonthlyCost
    };

    private static JsonObject AssetValues(AssetDto a) => new()
    {
        ["assetTag"] = a.AssetTag,
        ["assetType"] = a.AssetType,
        ["makeModel"] = a.MakeModel,
        ["serialNumber"] = a.SerialNumber
    };

    private static JsonObject IdCardValues(IdCardDto c) => new()
    {
        ["cardNumber"] = c.CardNumber,
        ["issuedDate"] = c.IssuedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
    };

    private Task AuditAsync(
        System.Data.Common.DbTransaction tx, ActorContext actor, string role, string action, string masterType,
        long id, string label, JsonObject? oldValues, JsonObject? newValues, CancellationToken ct)
    {
        var details = new JsonObject
        {
            ["action"] = action,
            ["masterType"] = masterType,
            ["item"] = label,
            ["id"] = id,
            ["old"] = oldValues,
            ["new"] = newValues
        };
        return _audit.AppendAsync(
            tx,
            new AuditEvent(null, AuditEventTypes.MasterRecordChanged, actor.UserId, actor.Name, role, null, null, null, null,
                details.ToJsonString(), _correlation.CorrelationId, _clock.GetUtcNow().UtcDateTime),
            ct);
    }
}
