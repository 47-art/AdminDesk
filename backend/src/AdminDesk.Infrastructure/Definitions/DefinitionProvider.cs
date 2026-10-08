using System.Collections.Concurrent;
using AdminDesk.Application.Definitions;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Definitions;

// Holds parsed definitions in memory. Entries are immutable and keyed by stored id; the
// active version of a module is the highest active one.
public sealed class DefinitionProvider : IDefinitionProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ConcurrentDictionary<long, IssuedDefinition> _byId = new();
    private volatile IReadOnlyList<IssuedDefinition>? _active;

    public DefinitionProvider(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public async Task<IssuedDefinition?> GetActiveAsync(string code, CancellationToken ct = default)
    {
        var active = await ListActiveAsync(ct);
        return active.FirstOrDefault(d => string.Equals(d.Definition.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IssuedDefinition?> GetByIdAsync(long definitionId, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(definitionId, out var cached))
        {
            return cached;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<IDefinitionRepository>().GetByIdAsync(definitionId, ct);
        return row is null ? null : _byId.GetOrAdd(row.Id, _ => ToIssued(row));
    }

    public async Task<IReadOnlyList<IssuedDefinition>> ListActiveAsync(CancellationToken ct = default)
    {
        var current = _active;
        if (current is not null)
        {
            return current;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IDefinitionRepository>().ListLatestAsync(ct);
        var loaded = rows.Select(row => _byId.GetOrAdd(row.Id, _ => ToIssued(row))).ToList();
        _active = loaded;
        return loaded;
    }

    public void Refresh() => _active = null;

    private static IssuedDefinition ToIssued(DefinitionRow row) =>
        new(row.Id, DefinitionJson.Parse(row.DefinitionJson));
}
