using System.Data.Common;

namespace AdminDesk.Application.Definitions;

public sealed record LimitRow(
    string ModuleCode,
    string StepKey,
    string LimitKey,
    long ValueMinor,
    string? Unit,
    bool IsSample);

// Limit values live in the database so an operator can change them; a restart never resets one.
public interface ILimitRepository
{
    // Adds the limit when no row exists for module, step and key; otherwise leaves the stored value alone.
    Task InsertIfMissingAsync(DbTransaction transaction, LimitRow limit, CancellationToken ct);

    Task<IReadOnlyList<LimitRow>> ListForModuleAsync(string moduleCode, CancellationToken ct);

    Task<IReadOnlyList<LimitRow>> ListAllAsync(CancellationToken ct);
}
