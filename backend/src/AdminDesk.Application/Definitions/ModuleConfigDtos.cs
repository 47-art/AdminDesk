using System.Text.Json;

namespace AdminDesk.Application.Definitions;

// A rule as stored: a group (any / all) or a single rule (field, op and either value or limit).
// Money values in a rule are minor units, the same as in the stored form data.
public sealed record RuleNodeDto(
    IReadOnlyList<RuleNodeDto>? Any,
    IReadOnlyList<RuleNodeDto>? All,
    string? Field,
    string? Op,
    JsonElement? Value,
    string? Limit);

// A field a condition may look at, with the operators that make sense for its type.
public sealed record ConditionFieldChoiceDto(
    string Key,
    string Label,
    string Type,
    string? LookupKind,
    IReadOnlyList<string> Operators,
    bool AllowsLimit,
    IReadOnlyList<FieldOptionDto> Options);

public sealed record ConfigStepDto(
    string Key,
    string Name,
    string Type,
    string ActorLabel,
    RuleNodeDto? Condition,
    string? ConditionSummary,

    // False when the stored condition is an any / all group; saving a single rule replaces the group.
    bool ConditionEditable,
    IReadOnlyList<ConditionFieldChoiceDto> ConditionFields,
    IReadOnlyList<string> LimitKeys);

public sealed record ConfigLimitDto(string StepKey, string LimitKey, long ValueMinor, string? Unit, bool IsSample);

public sealed record ModuleConfigDto(
    string Code,
    string Name,
    string Category,
    int Version,
    IReadOnlyList<FieldDto> Fields,
    IReadOnlyList<ConfigStepDto> Steps,
    IReadOnlyList<ConfigLimitDto> Limits);

public sealed record UpdateLimitBody(string StepKey, string LimitKey, long ValueMinor);

// One rule as sent by the editor. Exactly one of value and limit is given, except for the operators
// that take neither (isEmpty, isNotEmpty).
public sealed record ConditionInput(string? Field, string? Op, JsonElement? Value, string? Limit);

// A null condition clears the step's condition.
public sealed record SaveConditionBody(int BaseVersion, ConditionInput? Condition);

public sealed record SaveConditionResult(int Version);
