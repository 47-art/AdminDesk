using AdminDesk.Domain.Definitions;

namespace AdminDesk.Application.Definitions;

// A definition together with the id of the stored version it was read from.
public sealed record IssuedDefinition(long Id, ModuleDefinition Definition);

// In-memory view of the stored definitions. Entries never change once loaded; Refresh
// drops them so the next read sees newly synced versions.
public interface IDefinitionProvider
{
    // The highest active version of a module, or null when the module is unknown.
    Task<IssuedDefinition?> GetActiveAsync(string code, CancellationToken ct = default);

    // Any issued version, so a request can stay on the version it was created with.
    Task<IssuedDefinition?> GetByIdAsync(long definitionId, CancellationToken ct = default);

    Task<IReadOnlyList<IssuedDefinition>> ListActiveAsync(CancellationToken ct = default);

    void Refresh();
}
