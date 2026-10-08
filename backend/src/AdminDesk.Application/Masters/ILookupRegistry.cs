namespace AdminDesk.Application.Masters;

// One searchable list of reference data, identified by a camelCase kind.
public interface ILookupProvider
{
    string Kind { get; }

    Task<IReadOnlyList<LookupItem>> SearchAsync(string q, int take, CancellationToken ct);

    Task<LookupItem?> GetAsync(long id, CancellationToken ct);

    Task<bool> ExistsAsync(long id, CancellationToken ct);
}

public interface ILookupRegistry
{
    IReadOnlyCollection<string> Kinds { get; }

    bool IsRegistered(string kind);

    ILookupProvider? Find(string kind);

    Task<bool> ExistsAsync(string kind, long id, CancellationToken ct);
}

// Built from every registered provider; a second provider for the same kind is a startup error.
public sealed class LookupRegistry : ILookupRegistry
{
    private readonly Dictionary<string, ILookupProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public LookupRegistry(IEnumerable<ILookupProvider> providers)
    {
        foreach (var provider in providers)
        {
            if (!_providers.TryAdd(provider.Kind, provider))
            {
                throw new InvalidOperationException($"Lookup kind '{provider.Kind}' is registered more than once.");
            }
        }
    }

    public IReadOnlyCollection<string> Kinds => _providers.Keys;

    public bool IsRegistered(string kind) => _providers.ContainsKey(kind);

    public ILookupProvider? Find(string kind) => _providers.GetValueOrDefault(kind);

    public Task<bool> ExistsAsync(string kind, long id, CancellationToken ct) =>
        Find(kind) is { } provider ? provider.ExistsAsync(id, ct) : Task.FromResult(false);
}
