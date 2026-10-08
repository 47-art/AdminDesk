using System.Globalization;
using System.Text.Json;
using AdminDesk.Application.Masters;
using AdminDesk.Domain.Definitions;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Money;
using AdminDesk.SharedKernel.Responses;

namespace AdminDesk.Application.Engine;

// Checks the values a client sends against the definition, in full: a request is created
// in one step, so every rule is enforced at that moment. The returned dictionaries hold the
// normalised values that are stored and fed to the rule engine: money as integer paise,
// numbers as decimal, yes/no as bool, multi select as string[], lookups as long and
// everything else as string. Empty optional values are left out.
public sealed class DefinitionPayloadValidator
{
    private const int DefaultTextLimit = 200;
    private const int DefaultLongTextLimit = 4000;
    private const int MaxSuppliedKeys = 100;
    public const int RemarksLimit = 1000;

    private readonly ILookupRegistry _lookups;

    public DefinitionPayloadValidator(ILookupRegistry lookups)
    {
        _lookups = lookups;
    }

    // Validates the common fields and the form values together so one response lists every problem.
    public async Task<IReadOnlyDictionary<string, object?>> ValidateAsync(
        ModuleDefinition definition,
        CommonFields common,
        DateOnly requestDate,
        IReadOnlyDictionary<string, JsonElement>? payload,
        CancellationToken ct)
    {
        var errors = new List<FieldError>();
        await CheckCommonAsync(common, requestDate, errors, ct);
        var values = await CheckFieldsAsync(definition.Fields, payload, errors, ct);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
        return values;
    }

    // Validates the values submitted when a task step is completed. Errors are keyed by the
    // capture field key; a step that declares no capture fields rejects any captured key.
    public async Task<IReadOnlyDictionary<string, object?>> ValidateCapturedAsync(
        IReadOnlyList<FieldDefinition>? captureFields,
        IReadOnlyDictionary<string, JsonElement>? captured,
        CancellationToken ct)
    {
        var errors = new List<FieldError>();
        var values = await CheckFieldsAsync(captureFields ?? Array.Empty<FieldDefinition>(), captured, errors, ct);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
        return values;
    }

    private async Task CheckCommonAsync(CommonFields common, DateOnly requestDate, List<FieldError> errors, CancellationToken ct)
    {
        if (!Enum.IsDefined(common.Priority))
        {
            errors.Add(Error("priority", "Choose a valid priority."));
        }
        if (common.RequiredDate is { } required && required < requestDate)
        {
            errors.Add(Error("requiredDate", "Choose a date that is not in the past."));
        }
        if (common.Remarks is { } remarks && remarks.Length > RemarksLimit)
        {
            errors.Add(Error("remarks", $"Keep the remarks to {RemarksLimit} characters or fewer."));
        }
        await CheckCommonLookupAsync("projectId", "project", common.ProjectId, errors, ct);
        await CheckCommonLookupAsync("locationId", "location", common.LocationId, errors, ct);
        await CheckCommonLookupAsync("costCentreId", "costCentre", common.CostCentreId, errors, ct);
    }

    private async Task CheckCommonLookupAsync(string field, string kind, int? id, List<FieldError> errors, CancellationToken ct)
    {
        if (id is { } value && !await _lookups.ExistsAsync(kind, value, ct))
        {
            errors.Add(Error(field, "Choose a value from the list."));
        }
    }

    private async Task<IReadOnlyDictionary<string, object?>> CheckFieldsAsync(
        IReadOnlyList<FieldDefinition> fields,
        IReadOnlyDictionary<string, JsonElement>? supplied,
        List<FieldError> errors,
        CancellationToken ct)
    {
        var values = new Dictionary<string, object?>();
        var input = supplied ?? new Dictionary<string, JsonElement>();

        if (input.Count > MaxSuppliedKeys)
        {
            errors.Add(Error("payload", "Too many values were sent."));
            return values;
        }

        foreach (var key in input.Keys)
        {
            if (fields.All(f => f.Key != key))
            {
                errors.Add(Error(key, "This field is not part of the form."));
            }
        }

        foreach (var field in fields)
        {
            input.TryGetValue(field.Key, out var raw);
            var missing = !input.ContainsKey(field.Key) || IsBlank(raw);
            if (missing)
            {
                if (field.Required)
                {
                    errors.Add(Error(field.Key, $"Enter {Lower(field.Label)}."));
                }
                continue;
            }

            var before = errors.Count;
            var normalised = await CheckFieldAsync(field, raw, errors, ct);
            if (errors.Count == before)
            {
                values[field.Key] = normalised;
            }
        }
        return values;
    }

    private static bool IsBlank(JsonElement raw) => raw.ValueKind switch
    {
        JsonValueKind.Undefined => true,
        JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(raw.GetString()),
        JsonValueKind.Array => raw.GetArrayLength() == 0,
        _ => false
    };

    // One rule per field type, shared by form fields and capture fields.
    private async Task<object?> CheckFieldAsync(FieldDefinition field, JsonElement raw, List<FieldError> errors, CancellationToken ct)
    {
        switch (field.Type)
        {
            case FieldType.Text:
            case FieldType.LongText:
            {
                if (raw.ValueKind != JsonValueKind.String)
                {
                    errors.Add(Error(field.Key, "Enter text."));
                    return null;
                }
                var text = raw.GetString()!.Trim();
                var limit = field.MaxLength ?? (field.Type == FieldType.LongText ? DefaultLongTextLimit : DefaultTextLimit);
                if (limit is { } max && text.Length > max)
                {
                    errors.Add(Error(field.Key, $"Keep {Lower(field.Label)} to {max} characters or fewer."));
                    return null;
                }
                return text;
            }

            case FieldType.Number:
            {
                if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetDecimal(out var number))
                {
                    errors.Add(Error(field.Key, "Enter a number."));
                    return null;
                }
                return CheckRange(field, number, errors) ? number : null;
            }

            case FieldType.Money:
            {
                if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetDecimal(out var rupees))
                {
                    errors.Add(Error(field.Key, "Enter an amount."));
                    return null;
                }
                if (decimal.Round(rupees, 2) != rupees)
                {
                    errors.Add(Error(field.Key, "Enter an amount with at most two decimal places."));
                    return null;
                }
                if (!CheckRange(field, rupees, errors))
                {
                    return null;
                }
                try
                {
                    return MoneyConverter.ToMinor(rupees);
                }
                catch (OverflowException)
                {
                    errors.Add(Error(field.Key, "Enter a smaller amount."));
                    return null;
                }
            }

            case FieldType.Date:
            {
                if (raw.ValueKind != JsonValueKind.String ||
                    !DateOnly.TryParseExact(raw.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    errors.Add(Error(field.Key, "Enter a valid date."));
                    return null;
                }
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            case FieldType.DateTime:
            {
                if (raw.ValueKind != JsonValueKind.String ||
                    !DateTimeOffset.TryParse(raw.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
                    !raw.GetString()!.Contains('T'))
                {
                    errors.Add(Error(field.Key, "Enter a valid date and time."));
                    return null;
                }
                return raw.GetString()!.Trim();
            }

            case FieldType.YesNo:
            {
                if (raw.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors.Add(Error(field.Key, "Choose yes or no."));
                    return null;
                }
                return raw.GetBoolean();
            }

            case FieldType.Select:
            {
                var choice = raw.ValueKind == JsonValueKind.String ? raw.GetString() : null;
                if (choice is null || field.Options?.Any(o => o.Value == choice) != true)
                {
                    errors.Add(Error(field.Key, "Choose one of the listed options."));
                    return null;
                }
                return choice;
            }

            case FieldType.MultiSelect:
            {
                if (raw.ValueKind != JsonValueKind.Array)
                {
                    errors.Add(Error(field.Key, "Choose from the listed options."));
                    return null;
                }
                var chosen = new List<string>();
                foreach (var item in raw.EnumerateArray())
                {
                    var option = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                    if (option is null || field.Options?.Any(o => o.Value == option) != true || chosen.Contains(option))
                    {
                        errors.Add(Error(field.Key, "Choose each listed option at most once."));
                        return null;
                    }
                    chosen.Add(option);
                }
                return chosen.ToArray();
            }

            case FieldType.Lookup:
            {
                var kind = field.LookupKind;
                if (kind is null || !_lookups.IsRegistered(kind))
                {
                    errors.Add(Error(field.Key, "This field cannot be used."));
                    return null;
                }
                if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt64(out var id) || !await _lookups.ExistsAsync(kind, id, ct))
                {
                    errors.Add(Error(field.Key, "Choose a value from the list."));
                    return null;
                }
                return id;
            }

            default:
                errors.Add(Error(field.Key, "This field cannot be used."));
                return null;
        }
    }

    private static bool CheckRange(FieldDefinition field, decimal value, List<FieldError> errors)
    {
        if (field.Min is { } min && value < min)
        {
            errors.Add(Error(field.Key, $"Enter a {Lower(field.Label)} of at least {min.ToString("0.##", CultureInfo.InvariantCulture)}."));
            return false;
        }
        if (field.Max is { } max && value > max)
        {
            errors.Add(Error(field.Key, $"Enter a {Lower(field.Label)} of at most {max.ToString("0.##", CultureInfo.InvariantCulture)}."));
            return false;
        }
        return true;
    }

    private static FieldError Error(string field, string message) => new() { Field = field, Message = message };

    private static string Lower(string label) => label.Length == 0 ? label : char.ToLowerInvariant(label[0]) + label[1..];
}
