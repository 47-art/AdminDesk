using AdminDesk.Domain.Definitions;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;

namespace AdminDesk.Application.Definitions;

public interface IModuleCatalogService
{
    Task<IReadOnlyList<ModuleSummaryDto>> ListAsync(CancellationToken ct);

    // The active version of a module.
    Task<ModuleDefinitionDto> GetAsync(string code, CancellationToken ct);

    // Any issued version, for a request that is pinned to it.
    Task<ModuleDefinitionDto> GetByIdAsync(long definitionId, CancellationToken ct);
}

public sealed class ModuleCatalogService : IModuleCatalogService
{
    private const string DefaultSection = "Details";

    private readonly IDefinitionProvider _provider;

    public ModuleCatalogService(IDefinitionProvider provider)
    {
        _provider = provider;
    }

    public async Task<IReadOnlyList<ModuleSummaryDto>> ListAsync(CancellationToken ct)
    {
        var active = await _provider.ListActiveAsync(ct);
        return active
            .Select(d => d.Definition)
            .OrderBy(d => CategoryOrder(d.Category))
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new ModuleSummaryDto(d.Code, d.Name, d.Description, d.Category, d.Icon, d.Prefix))
            .ToList();
    }

    public async Task<ModuleDefinitionDto> GetAsync(string code, CancellationToken ct)
    {
        var issued = await _provider.GetActiveAsync(code, ct)
            ?? throw new NotFoundException($"Unknown module '{code}'.");
        return ToDto(issued);
    }

    public async Task<ModuleDefinitionDto> GetByIdAsync(long definitionId, CancellationToken ct)
    {
        var issued = await _provider.GetByIdAsync(definitionId, ct)
            ?? throw new NotFoundException("Unknown module definition.");
        return ToDto(issued);
    }

    // "Reporting manager", "Requester", or the role names joined with " or " (for example "Admin or Store").
    public static string ActorLabelFor(StepDefinition step)
    {
        var actor = step.Actor;
        if (actor is null)
        {
            return string.Empty;
        }
        if (actor.ReportingManager)
        {
            return "Reporting manager";
        }
        if (actor.Requester)
        {
            return "Requester";
        }
        return string.Join(" or ", actor.Roles ?? Array.Empty<string>());
    }

    // Maps one field; used for form fields and capture fields alike so both look the same everywhere.
    public static FieldDto FieldDtoFor(FieldDefinition field) =>
        new(
            field.Key,
            field.Label,
            field.Type.ToString(),
            field.Required,
            field.MaxLength,
            field.Min,
            field.Max,
            field.HelpText,
            field.FullWidth,
            (field.Options ?? Array.Empty<FieldOption>()).Select(o => new FieldOptionDto(o.Value, o.Label)).ToList(),
            field.LookupKind);

    public static ModuleDefinitionDto ToDto(IssuedDefinition issued)
    {
        var def = issued.Definition;

        // Sections appear in the order their title first appears among the fields.
        var sections = new List<SectionDto>();
        foreach (var group in def.Fields.GroupBy(f => string.IsNullOrWhiteSpace(f.Section) ? DefaultSection : f.Section!))
        {
            sections.Add(new SectionDto(group.Key, group.Select(FieldDtoFor).ToList()));
        }

        var steps = def.Steps
            .Select(s => new StepSummaryDto(
                s.Key,
                s.Name,
                s.Type.ToString(),
                ActorLabelFor(s),
                s.ActionLabel,
                (s.CaptureFields ?? Array.Empty<FieldDefinition>()).Select(FieldDtoFor).ToList(),
                s.RequiresDocument))
            .ToList();

        return new ModuleDefinitionDto(
            issued.Id, def.Code, def.Version, def.Name, def.Description, def.Category, def.Icon, def.Prefix, sections, steps);
    }

    private static int CategoryOrder(string category)
    {
        var index = Array.IndexOf(Categories.All, category);
        return index < 0 ? int.MaxValue : index;
    }
}
