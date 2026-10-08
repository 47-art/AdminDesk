namespace AdminDesk.Application.Definitions;

public sealed record ModuleSummaryDto(
    string Code,
    string Name,
    string? Description,
    string Category,
    string? Icon,
    string Prefix);

public sealed record FieldOptionDto(string Value, string Label);

public sealed record FieldDto(
    string Key,
    string Label,
    string Type,
    bool Required,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    string? HelpText,
    bool FullWidth,
    IReadOnlyList<FieldOptionDto> Options,
    string? LookupKind);

public sealed record SectionDto(string Title, IReadOnlyList<FieldDto> Fields);

public sealed record StepSummaryDto(
    string Key,
    string Name,
    string Type,
    string ActorLabel,
    string? ActionLabel,
    IReadOnlyList<FieldDto> CaptureFields);

public sealed record ModuleDefinitionDto(
    long DefinitionId,
    string Code,
    int Version,
    string Name,
    string? Description,
    string Category,
    string? Icon,
    string Prefix,
    IReadOnlyList<SectionDto> Sections,
    IReadOnlyList<StepSummaryDto> Steps);
