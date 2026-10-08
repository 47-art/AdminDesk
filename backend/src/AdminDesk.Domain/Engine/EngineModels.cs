using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Domain.Engine;

// One person or role allowed to act on a step. Exactly one of the two is set.
public sealed record ActorSlot(string? RoleName, int? EmployeeId);

// A step of one request as the engine sees it.
public sealed record PlannedStep(
    int Seq,
    string Key,
    string Name,
    StepType Type,
    StepState State,
    IReadOnlyList<ActorSlot> ActorSlots);

// Normalised values by field key. Money is integer paise (long), number is decimal,
// text, select, date and dateTime are string, yesNo is bool, multiSelect is string[] and
// lookups are long. Captured values of finished steps appear under "stepKey.fieldKey".
public sealed class FieldValues
{
    public static readonly FieldValues Empty = new(new Dictionary<string, object?>());

    private readonly IReadOnlyDictionary<string, object?> _values;

    public FieldValues(IReadOnlyDictionary<string, object?> values)
    {
        _values = values;
    }

    public bool TryGet(string key, out object? value) => _values.TryGetValue(key, out value);

    public IReadOnlyDictionary<string, object?> All => _values;

    public FieldValues With(IEnumerable<KeyValuePair<string, object?>> more)
    {
        var merged = new Dictionary<string, object?>(_values);
        foreach (var pair in more)
        {
            merged[pair.Key] = pair.Value;
        }
        return new FieldValues(merged);
    }
}

// Limit values in minor units (paise for money, plain count for counts).
public sealed class LimitSet
{
    public static readonly LimitSet Empty = new(Array.Empty<LimitEntry>());

    private readonly Dictionary<(string, string), LimitEntry> _entries;

    public LimitSet(IEnumerable<LimitEntry> entries)
    {
        _entries = entries.ToDictionary(e => (e.StepKey, e.LimitKey));
    }

    public bool TryGet(string stepKey, string limitKey, out LimitEntry entry) =>
        _entries.TryGetValue((stepKey, limitKey), out entry!);
}

public sealed record LimitEntry(string StepKey, string LimitKey, long ValueMinor, string? Unit);

public sealed record RequesterInfo(int EmployeeId, int? ManagerEmployeeId, IReadOnlySet<string> Roles);

// Outcome of walking the steps forward: the updated list and the seq now waiting for action.
public sealed record ForwardResult(IReadOnlyList<PlannedStep> Steps, int? ActiveSeq);
