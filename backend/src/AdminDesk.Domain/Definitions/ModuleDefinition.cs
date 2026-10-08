using System.Text.Json;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Domain.Definitions;

// Immutable description of one module, read from a definition file.
//
// Step semantics: a step without a condition is always required. A step with a
// condition starts as undecided (Upcoming) and is decided when it becomes the next
// step to activate: Pending when the condition is true, otherwise NotRequired.
// A required step that nobody can act on simply waits. There is no step flag for
// leaving a step out and no automatic skipping.
public sealed record ModuleDefinition
{
    public string Code { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Category { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string Prefix { get; init; } = string.Empty;
    public string? Subject { get; init; }
    public IReadOnlyList<FieldDefinition> Fields { get; init; } = Array.Empty<FieldDefinition>();
    public IReadOnlyList<StepDefinition> Steps { get; init; } = Array.Empty<StepDefinition>();
    public IReadOnlyList<LimitDefinition> Limits { get; init; } = Array.Empty<LimitDefinition>();
}

public sealed record FieldOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed record FieldDefinition
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public FieldType Type { get; init; }
    public bool Required { get; init; }
    public int? MaxLength { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public string? Section { get; init; }
    public bool FullWidth { get; init; }
    public string? HelpText { get; init; }
    public IReadOnlyList<FieldOption>? Options { get; init; }

    // A registered lookup kind such as employee or costCentre, matched without regard to case.
    public string? LookupKind { get; init; }
}

public sealed record StepActor
{
    public bool ReportingManager { get; init; }
    public bool Requester { get; init; }
    public IReadOnlyList<string>? Roles { get; init; }
}

public sealed record StepDefinition
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public StepType Type { get; init; }
    public StepActor? Actor { get; init; }
    public RuleNode? Condition { get; init; }
    public string? ActionLabel { get; init; }
    public IReadOnlyList<FieldDefinition>? CaptureFields { get; init; }

    // When true, the request can no longer be cancelled once this step is done.
    public bool LocksCancel { get; init; }
}

// A group (Any or All) or a leaf rule (Field, Op and either Value or Limit).
public sealed record RuleNode
{
    public IReadOnlyList<RuleNode>? Any { get; init; }
    public IReadOnlyList<RuleNode>? All { get; init; }
    public string? Field { get; init; }
    public RuleOperator? Op { get; init; }
    public JsonElement? Value { get; init; }
    public string? Limit { get; init; }
}

public sealed record LimitDefinition
{
    public string StepKey { get; init; } = string.Empty;
    public string LimitKey { get; init; } = string.Empty;
    public long ValueMinor { get; init; }
    public string Unit { get; init; } = string.Empty;
}
