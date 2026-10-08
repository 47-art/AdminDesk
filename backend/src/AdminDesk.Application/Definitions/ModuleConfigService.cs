using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.Domain.Definitions;
using AdminDesk.SharedKernel.Actors;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Money;
using AdminDesk.SharedKernel.Responses;

namespace AdminDesk.Application.Definitions;

public interface IModuleConfigService
{
    // The latest version of every module, with its limits as stored now.
    Task<IReadOnlyList<ModuleConfigDto>> GetAllAsync(CancellationToken ct);

    Task<ModuleConfigDto> GetAsync(string code, CancellationToken ct);

    Task UpdateLimitAsync(ActorContext actor, string code, string stepKey, string limitKey, long valueMinor, CancellationToken ct);

    // Stores a new version of the module with only this step's condition changed; a null condition clears it.
    // Returns the version now in force.
    Task<int> SaveConditionAsync(
        ActorContext actor, string code, string stepKey, int baseVersion, ConditionInput? condition, CancellationToken ct);
}

// Reads the stored definitions for the configuration page and writes the two things an owner may
// change without a release: limit amounts and step conditions. Limits are updated in place and apply to
// requests created afterwards; a condition change is a new definition version.
public sealed class ModuleConfigService : IModuleConfigService
{
    private static readonly string[] NumberOperators = { "eq", "neq", "gt", "gte", "lt", "lte", "isEmpty", "isNotEmpty" };
    private static readonly string[] TextOperators = { "eq", "neq", "in", "notIn", "isEmpty", "isNotEmpty" };
    private static readonly string[] YesNoOperators = { "eq", "neq" };
    private static readonly string[] LimitOperators = { "eq", "neq", "gt", "gte", "lt", "lte" };

    private readonly IDefinitionProvider _provider;
    private readonly IDefinitionRepository _definitions;
    private readonly ILimitRepository _limits;
    private readonly IAuditRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILookupRegistry _lookups;
    private readonly IActorAccessor _actorAccessor;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly TimeProvider _clock;

    public ModuleConfigService(
        IDefinitionProvider provider,
        IDefinitionRepository definitions,
        ILimitRepository limits,
        IAuditRepository audit,
        IUnitOfWork unitOfWork,
        ILookupRegistry lookups,
        IActorAccessor actorAccessor,
        ICorrelationIdAccessor correlation,
        TimeProvider clock)
    {
        _provider = provider;
        _definitions = definitions;
        _limits = limits;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _lookups = lookups;
        _actorAccessor = actorAccessor;
        _correlation = correlation;
        _clock = clock;
    }

    // ------------------------------------------------------------------ read

    public async Task<IReadOnlyList<ModuleConfigDto>> GetAllAsync(CancellationToken ct)
    {
        var active = await _provider.ListActiveAsync(ct);
        var limits = (await _limits.ListAllAsync(ct)).ToLookup(l => l.ModuleCode, StringComparer.Ordinal);
        return active
            .Select(d => d.Definition)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => ToDto(d, limits[d.Code].ToList()))
            .ToList();
    }

    public async Task<ModuleConfigDto> GetAsync(string code, CancellationToken ct)
    {
        var issued = await RequireModuleAsync(code, ct);
        var limits = await _limits.ListForModuleAsync(issued.Definition.Code, ct);
        return ToDto(issued.Definition, limits);
    }

    // ---------------------------------------------------------------- limits

    public async Task UpdateLimitAsync(
        ActorContext actor, string code, string stepKey, string limitKey, long valueMinor, CancellationToken ct)
    {
        RequireEditor(actor);
        if (valueMinor < 0)
        {
            throw new ValidationException("valueMinor", "A limit cannot be negative.");
        }
        if (valueMinor > MoneyConverter.MaxMinor)
        {
            throw new ValidationException("valueMinor", MoneyConverter.TooLargeMessage);
        }

        var issued = await RequireModuleAsync(code, ct);
        var moduleCode = issued.Definition.Code;
        if (await _limits.GetAsync(moduleCode, stepKey, limitKey, ct) is null)
        {
            throw new NotFoundException($"Module '{moduleCode}' has no limit '{limitKey}' on step '{stepKey}'.");
        }

        _actorAccessor.Use(actor.UserId);
        var now = _clock.GetUtcNow().UtcDateTime;
        await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            var previous = await _limits.UpdateValueAsync(tx, moduleCode, stepKey, limitKey, valueMinor, ct)
                ?? throw new NotFoundException($"Module '{moduleCode}' has no limit '{limitKey}' on step '{stepKey}'.");
            var details = new JsonObject
            {
                ["module"] = moduleCode,
                ["step"] = stepKey,
                ["limit"] = limitKey,
                ["old"] = previous,
                ["new"] = valueMinor
            };
            await AppendAuditAsync(tx, AuditEventTypes.LimitChanged, actor, stepKey, details, now, ct);
            return 0;
        }, ct);
    }

    // ------------------------------------------------------------- conditions

    public async Task<int> SaveConditionAsync(
        ActorContext actor, string code, string stepKey, int baseVersion, ConditionInput? condition, CancellationToken ct)
    {
        RequireEditor(actor);
        var issued = await RequireModuleAsync(code, ct);
        var def = issued.Definition;

        if (baseVersion != def.Version)
        {
            throw new ConflictException(
                "This module was changed by someone else. Reload the page and try again.", ErrorCodes.STATE_CONFLICT);
        }

        var stepIndex = def.Steps.ToList().FindIndex(s => s.Key == stepKey);
        if (stepIndex < 0)
        {
            throw new NotFoundException($"Module '{def.Code}' has no step '{stepKey}'.");
        }

        var row = await _definitions.GetByIdAsync(issued.Id, ct)
            ?? throw new NotFoundException($"Module '{code}' is not available.");
        var root = JsonNode.Parse(row.DefinitionJson)!.AsObject();
        var stepNode = FindStep(root, stepKey);
        var oldCondition = stepNode["condition"]?.DeepClone();

        JsonNode? newCondition = null;
        if (condition is not null)
        {
            newCondition = BuildCondition(def, stepIndex, condition);
        }
        else if (oldCondition is null)
        {
            // Nothing to clear.
            return def.Version;
        }

        var maxStored = await _definitions.MaxVersionAsync(def.Code, ct) ?? def.Version;
        var newVersion = Math.Max(maxStored, def.Version) + 1;

        if (newCondition is null)
        {
            stepNode.Remove("condition");
        }
        else
        {
            stepNode["condition"] = newCondition.DeepClone();
        }
        root["version"] = newVersion;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        CheckStored(json, def.Code, newVersion);

        var newRow = new DefinitionRow(
            0, row.Code, newVersion, row.Name, row.Category, row.Prefix, json,
            DefinitionHash.Of(Encoding.UTF8.GetBytes(json)));

        _actorAccessor.Use(actor.UserId);
        var now = _clock.GetUtcNow().UtcDateTime;
        await _unitOfWork.ExecuteInTransactionAsync(async (_, tx) =>
        {
            await _definitions.InsertAsync(tx, newRow, ct);
            var details = new JsonObject
            {
                ["module"] = def.Code,
                ["step"] = stepKey,
                ["old"] = oldCondition,
                ["new"] = newCondition,
                ["version"] = newVersion
            };
            await AppendAuditAsync(tx, AuditEventTypes.ConditionChanged, actor, stepKey, details, now, ct);
            return 0;
        }, ct);

        _provider.Refresh();
        return newVersion;
    }

    // ------------------------------------------------------------- internals

    private static void RequireEditor(ActorContext actor)
    {
        if (!actor.Roles.Overlaps(Roles.LimitEditors))
        {
            throw new ForbiddenException("You are not allowed to change module settings.");
        }
    }

    private async Task<IssuedDefinition> RequireModuleAsync(string code, CancellationToken ct) =>
        await _provider.GetActiveAsync(code, ct) ?? throw new NotFoundException($"Unknown module '{code}'.");

    private static JsonObject FindStep(JsonObject root, string stepKey)
    {
        var steps = root["steps"]?.AsArray() ?? throw new NotFoundException("The module has no steps.");
        foreach (var node in steps)
        {
            if (node is JsonObject step && step["key"]?.GetValue<string>() == stepKey)
            {
                return step;
            }
        }
        throw new NotFoundException($"Unknown step '{stepKey}'.");
    }

    private Task AppendAuditAsync(
        System.Data.Common.DbTransaction tx, string eventType, ActorContext actor, string stepKey, JsonObject details,
        DateTime now, CancellationToken ct)
    {
        var role = actor.Roles.FirstOrDefault(r => Roles.LimitEditors.Contains(r));
        return _audit.AppendAsync(
            tx,
            new AuditEvent(null, eventType, actor.UserId, actor.Name, role, stepKey, null, null, null,
                details.ToJsonString(), _correlation.CorrelationId, now),
            ct);
    }

    // The edited text must be a definition the startup loader would accept.
    private void CheckStored(string json, string code, int version)
    {
        ModuleDefinition parsed;
        try
        {
            parsed = DefinitionJson.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ValidationException("condition", $"The changed module is not valid: {ex.Message}");
        }

        var problems = DefinitionValidator.Validate(new[] { ($"{code} version {version}", parsed) }, _lookups.Kinds);
        if (problems.Count > 0)
        {
            throw new ValidationException("condition", string.Join(" ", problems));
        }
    }

    private static string CamelOp(RuleOperator op) => JsonNamingPolicy.CamelCase.ConvertName(op.ToString());

    private static IReadOnlyList<string> OperatorsFor(FieldType type) => type switch
    {
        FieldType.Number or FieldType.Money or FieldType.Date or FieldType.DateTime => NumberOperators,
        FieldType.YesNo => YesNoOperators,
        _ => TextOperators
    };

    private static bool AllowsLimit(FieldType type) => type is FieldType.Number or FieldType.Money;

    private static List<(string Key, string Label, FieldDefinition Field)> ChoicesFor(ModuleDefinition def, int stepIndex)
    {
        var choices = new List<(string, string, FieldDefinition)>();
        foreach (var field in def.Fields)
        {
            choices.Add((field.Key, field.Label, field));
        }
        for (var i = 0; i < stepIndex; i++)
        {
            var earlier = def.Steps[i];
            foreach (var capture in earlier.CaptureFields ?? Array.Empty<FieldDefinition>())
            {
                choices.Add(($"{earlier.Key}.{capture.Key}", $"{earlier.Name}: {capture.Label}", capture));
            }
        }
        return choices;
    }

    // Checks one rule from the editor and returns it as definition JSON.
    private static JsonNode BuildCondition(ModuleDefinition def, int stepIndex, ConditionInput input)
    {
        var step = def.Steps[stepIndex];
        var errors = new List<FieldError>();
        void Error(string field, string message) => errors.Add(new FieldError { Field = field, Message = message });

        var choices = ChoicesFor(def, stepIndex);
        var chosen = choices.FirstOrDefault(c => c.Key == input.Field);
        if (string.IsNullOrWhiteSpace(input.Field) || chosen.Field is null)
        {
            Error("condition.field", "Choose one of the fields offered for this step.");
        }

        RuleOperator? op = null;
        if (!string.IsNullOrWhiteSpace(input.Op) && Enum.TryParse<RuleOperator>(input.Op, true, out var parsed) &&
            Enum.IsDefined(parsed) && !int.TryParse(input.Op, out _))
        {
            op = parsed;
        }
        else
        {
            Error("condition.op", "Choose a valid operator.");
        }

        if (chosen.Field is not null && op is { } checkedOp)
        {
            var allowed = OperatorsFor(chosen.Field.Type);
            if (!allowed.Contains(CamelOp(checkedOp)))
            {
                Error("condition.op", $"This operator cannot be used with a {chosen.Field.Type} field.");
                op = null;
            }
        }

        var value = input.Value is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } v ? v : (JsonElement?)null;
        var limit = string.IsNullOrWhiteSpace(input.Limit) ? null : input.Limit;

        if (op is { } operatorChoice && chosen.Field is not null)
        {
            CheckOperands(step, def, chosen.Field, operatorChoice, value, limit, Error);
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors, errors[0].Message);
        }

        var node = new JsonObject
        {
            ["field"] = input.Field,
            ["op"] = CamelOp(op!.Value)
        };
        if (value is { } literal)
        {
            node["value"] = JsonNode.Parse(literal.GetRawText());
        }
        if (limit is not null)
        {
            node["limit"] = limit;
        }
        return node;
    }

    private static void CheckOperands(
        StepDefinition step, ModuleDefinition def, FieldDefinition field, RuleOperator op,
        JsonElement? value, string? limit, Action<string, string> error)
    {
        if (op is RuleOperator.IsEmpty or RuleOperator.IsNotEmpty)
        {
            if (value is not null)
            {
                error("condition.value", "This operator takes no value.");
            }
            if (limit is not null)
            {
                error("condition.limit", "This operator takes no limit.");
            }
            return;
        }

        if (op is RuleOperator.In or RuleOperator.NotIn)
        {
            if (limit is not null)
            {
                error("condition.limit", "A list cannot be compared with a limit.");
            }
            if (value is not { ValueKind: JsonValueKind.Array } list || list.GetArrayLength() == 0)
            {
                error("condition.value", "Give a list with at least one value.");
            }
            else
            {
                foreach (var item in list.EnumerateArray())
                {
                    CheckLiteral(field, item, error);
                }
            }
            return;
        }

        if (value is not null && limit is not null)
        {
            error("condition.value", "Give either a value or a limit, not both.");
            return;
        }
        if (value is null && limit is null)
        {
            error("condition.value", AllowsLimit(field.Type) ? "Give a value or choose a limit." : "Give a value.");
            return;
        }

        if (limit is not null)
        {
            if (!AllowsLimit(field.Type) || !LimitOperators.Contains(CamelOp(op)))
            {
                error("condition.limit", "A limit can only be compared with a number or money field.");
            }
            else if (!def.Limits.Any(l => l.StepKey == step.Key && l.LimitKey == limit))
            {
                error("condition.limit", $"'{limit}' is not a limit defined for this step.");
            }
            return;
        }

        CheckLiteral(field, value!.Value, error);
    }

    private static void CheckLiteral(FieldDefinition field, JsonElement value, Action<string, string> error)
    {
        var ok = field.Type switch
        {
            FieldType.Money => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var rupees)
                && decimal.Round(rupees, 2) == rupees && MoneyConverter.IsWithinLimit(rupees),
            FieldType.Lookup => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            FieldType.Number => value.ValueKind == JsonValueKind.Number,
            FieldType.YesNo => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => value.ValueKind == JsonValueKind.String
        };
        if (!ok)
        {
            error("condition.value", field.Type switch
            {
                FieldType.Money => "Enter an amount in rupees (up to 1,000,000,000) with at most two decimals.",
                FieldType.Lookup => "Enter the whole-number id.",
                FieldType.Number => "Enter a number.",
                FieldType.YesNo => "Choose yes or no.",
                _ => "Enter text."
            });
        }
    }

    // ------------------------------------------------------------------ DTOs

    private static ModuleConfigDto ToDto(ModuleDefinition def, IReadOnlyList<LimitRow> limits)
    {
        var steps = new List<ConfigStepDto>();
        for (var i = 0; i < def.Steps.Count; i++)
        {
            var step = def.Steps[i];
            var choices = ChoicesFor(def, i)
                .Select(c => new ConditionFieldChoiceDto(
                    c.Key,
                    c.Label,
                    c.Field.Type.ToString(),
                    c.Field.LookupKind,
                    OperatorsFor(c.Field.Type),
                    AllowsLimit(c.Field.Type),
                    (c.Field.Options ?? Array.Empty<FieldOption>()).Select(o => new FieldOptionDto(o.Value, o.Label)).ToList()))
                .ToList();
            var isLeaf = step.Condition is null || (step.Condition.Any is null && step.Condition.All is null);
            steps.Add(new ConfigStepDto(
                step.Key,
                step.Name,
                step.Type.ToString(),
                ModuleCatalogService.ActorLabelFor(step),
                step.Condition is null ? null : ToRuleDto(step.Condition),
                step.Condition is null ? null : Summarise(step.Condition),
                isLeaf,
                choices,
                def.Limits.Where(l => l.StepKey == step.Key).Select(l => l.LimitKey).ToList()));
        }

        return new ModuleConfigDto(
            def.Code,
            def.Name,
            def.Category,
            def.Version,
            def.Fields.Select(ModuleCatalogService.FieldDtoFor).ToList(),
            steps,
            limits.Select(l => new ConfigLimitDto(l.StepKey, l.LimitKey, l.ValueMinor, l.Unit, l.IsSample)).ToList());
    }

    private static RuleNodeDto ToRuleDto(RuleNode node) =>
        new(
            node.Any?.Select(ToRuleDto).ToList(),
            node.All?.Select(ToRuleDto).ToList(),
            node.Field,
            node.Op is { } op ? CamelOp(op) : null,
            node.Value,
            node.Limit);

    private static string Summarise(RuleNode node)
    {
        if (node.Any is { Count: > 0 })
        {
            return "any of (" + string.Join(" or ", node.Any.Select(Summarise)) + ")";
        }
        if (node.All is { Count: > 0 })
        {
            return "all of (" + string.Join(" and ", node.All.Select(Summarise)) + ")";
        }

        var op = node.Op is { } o ? OperatorWords(o) : "?";
        var operand = node.Limit is not null
            ? $"limit {node.Limit}"
            : node.Value is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } v ? v.GetRawText() : string.Empty;
        return $"{node.Field} {op} {operand}".TrimEnd();
    }

    private static string OperatorWords(RuleOperator op) => op switch
    {
        RuleOperator.Eq => "is",
        RuleOperator.Neq => "is not",
        RuleOperator.Gt => "is above",
        RuleOperator.Gte => "is at least",
        RuleOperator.Lt => "is below",
        RuleOperator.Lte => "is at most",
        RuleOperator.In => "is one of",
        RuleOperator.NotIn => "is none of",
        RuleOperator.IsEmpty => "is empty",
        _ => "is not empty"
    };
}
