using System.Data.Common;

namespace AdminDesk.Application.Definitions;

public sealed record DefinitionRow(
    long Id,
    string Code,
    int Version,
    string Name,
    string? Category,
    string Prefix,
    string DefinitionJson,
    string ContentHash);

// Stored definition versions. Rows are only ever added; a version is never changed or removed.
public interface IDefinitionRepository
{
    // Returns the new row id.
    Task<long> InsertAsync(DbTransaction transaction, DefinitionRow row, CancellationToken ct);

    // Highest stored version of a module, removed rows included; null when none exists.
    Task<int?> MaxVersionAsync(string code, CancellationToken ct);

    // Any stored row by id, active or not, so a request pinned to it always opens.
    Task<DefinitionRow?> GetByIdAsync(long id, CancellationToken ct);

    // The highest active version of every module.
    Task<IReadOnlyList<DefinitionRow>> ListLatestAsync(CancellationToken ct);

    Task<string?> GetHashAsync(string code, int version, CancellationToken ct);
}
