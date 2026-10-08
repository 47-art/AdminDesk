using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Application.Masters;
using AdminDesk.Domain.Definitions;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Definitions;

// Loads every definition file, checks the whole set, and only then stores what is new.
// Storing is insert-only: a higher version adds a row, an equal or lower version changes
// nothing, and limits that already exist keep the value held in the database.
public sealed class DefinitionSyncTask : IStartupTask
{
    private readonly DefinitionFileReader _reader;
    private readonly ILookupRegistry _lookups;
    private readonly IDefinitionRepository _definitions;
    private readonly ILimitRepository _limits;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDefinitionProvider _provider;
    private readonly ILogger<DefinitionSyncTask> _logger;

    public DefinitionSyncTask(
        DefinitionFileReader reader,
        ILookupRegistry lookups,
        IDefinitionRepository definitions,
        ILimitRepository limits,
        IUnitOfWork unitOfWork,
        IDefinitionProvider provider,
        ILogger<DefinitionSyncTask> logger)
    {
        _reader = reader;
        _lookups = lookups;
        _definitions = definitions;
        _limits = limits;
        _unitOfWork = unitOfWork;
        _provider = provider;
        _logger = logger;
    }

    public int Order => 30;

    public async Task RunAsync(CancellationToken ct)
    {
        var read = _reader.Read();
        var problems = new List<string>(read.Problems);
        problems.AddRange(DefinitionValidator.Validate(
            read.Definitions.Select(d => (d.File, d.Definition)).ToList(),
            _lookups.Kinds));
        if (problems.Count > 0)
        {
            throw DefinitionValidator.ToException(problems);
        }

        // Decide first, write afterwards, so the write transaction stays short.
        var toInsert = new List<LoadedDefinition>();
        var withLimits = new List<LoadedDefinition>();
        foreach (var loaded in read.Definitions)
        {
            var def = loaded.Definition;
            var max = await _definitions.MaxVersionAsync(def.Code, ct);
            if (max is null || def.Version > max)
            {
                toInsert.Add(loaded);
                withLimits.Add(loaded);
            }
            else if (def.Version == max)
            {
                var storedHash = await _definitions.GetHashAsync(def.Code, def.Version, ct);
                if (!string.Equals(storedHash, loaded.ContentHash, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Definition '{File}' (module {Code}, version {Version}) changed without a version bump, ignored",
                        loaded.File, def.Code, def.Version);
                }
                withLimits.Add(loaded);
            }
        }

        await _unitOfWork.ExecuteInTransactionAsync(async (_, transaction) =>
        {
            foreach (var loaded in toInsert)
            {
                var def = loaded.Definition;
                await _definitions.InsertAsync(
                    transaction,
                    new DefinitionRow(0, def.Code, def.Version, def.Name, def.Category, def.Prefix, loaded.Json, loaded.ContentHash),
                    ct);
                _logger.LogInformation("Definition stored: {Code} version {Version}", def.Code, def.Version);
            }

            foreach (var loaded in withLimits)
            {
                foreach (var limit in loaded.Definition.Limits)
                {
                    await _limits.InsertIfMissingAsync(
                        transaction,
                        new LimitRow(loaded.Definition.Code, limit.StepKey, limit.LimitKey, limit.ValueMinor, limit.Unit, true),
                        ct);
                }
            }
            return 0;
        }, ct);

        _provider.Refresh();
    }
}
