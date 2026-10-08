using System.Text.Json;
using System.Text.RegularExpressions;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;

namespace AdminDesk.Domain.Definitions;

// Checks a whole set of definitions and reports every problem found, never just the first.
public static class DefinitionValidator
{
    private static readonly Regex CodePattern = new("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex StepKeyPattern = new("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex FieldKeyPattern = new("^[a-z][A-Za-z0-9]*$", RegexOptions.Compiled);
    private static readonly Regex PrefixPattern = new("^[A-Z]{2,5}$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderPattern = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    // The engine supplies only payload values and captured values to rules, never these.
    private static readonly string[] CommonFieldNames =
        { "projectId", "locationId", "costCentreId", "requiredDate", "priority", "remarks" };

    private static readonly string[] Units = { "INR", "count" };

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<(string file, ModuleDefinition def)> definitions,
        IReadOnlyCollection<string> knownLookupKinds)
    {
        var problems = new List<string>();
        var lookupKinds = new HashSet<string>(knownLookupKinds, StringComparer.OrdinalIgnoreCase);

        var prefixOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        var codeOwner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (file, def) in definitions)
        {
            ValidateOne(file, def, lookupKinds, problems);

            if (!string.IsNullOrEmpty(def.Prefix) && !prefixOwner.TryAdd(def.Prefix, file))
            {
                problems.Add($"Definition '{file}': prefix '{def.Prefix}' is already used by '{prefixOwner[def.Prefix]}'");
            }
            if (!string.IsNullOrEmpty(def.Code) && !codeOwner.TryAdd(def.Code, file))
            {
                problems.Add($"Definition '{file}': code '{def.Code}' is already used by '{codeOwner[def.Code]}'");
            }
        }

        return problems;
    }

    // One exception carrying every problem, one per line, starting with the count.
    public static DefinitionLoadException ToException(IReadOnlyList<string> problems) =>
        new($"{problems.Count} definition problem(s):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");

    private static void ValidateOne(string file, ModuleDefinition def, HashSet<string> lookupKinds, List<string> problems)
    {
        string Where() => $"Definition '{file}'";

        if (!CodePattern.IsMatch(def.Code ?? string.Empty))
        {
            problems.Add($"{Where()}: code '{def.Code}' must be lowercase words joined by hyphens");
        }
        if (def.Version < 1)
        {
            problems.Add($"{Where()}: version must be a whole number of at least 1");
        }
        if (string.IsNullOrWhiteSpace(def.Name))
        {
            problems.Add($"{Where()}: name is required");
        }
        if (!PrefixPattern.IsMatch(def.Prefix ?? string.Empty))
        {
            problems.Add($"{Where()}: prefix '{def.Prefix}' must be 2 to 5 capital letters");
        }
        if (!Categories.All.Contains(def.Category))
        {
            problems.Add($"{Where()}: category '{def.Category}' is not one of: {string.Join(", ", Categories.All)}");
        }

        var requiredCommon = def.RequiredCommonFields ?? Array.Empty<CommonFieldKey>();
        if (requiredCommon.Distinct().Count() != requiredCommon.Count)
        {
            problems.Add($"{Where()}: requiredCommonFields lists the same field more than once");
        }

        var fields = def.Fields ?? Array.Empty<FieldDefinition>();
        var steps = def.Steps ?? Array.Empty<StepDefinition>();
        var limits = def.Limits ?? Array.Empty<LimitDefinition>();

        if (steps.Count == 0)
        {
            problems.Add($"{Where()}: at least one step is required");
        }

        var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var earlier = fieldKeys.ToList();
            if (!string.IsNullOrEmpty(field.Key) && !fieldKeys.Add(field.Key))
            {
                problems.Add($"{Where()}: field key '{field.Key}' is used more than once");
            }
            ValidateField(file, $"field '{field.Key}'", field, lookupKinds, problems);
            ValidateShowWhen(file, $"field '{field.Key}'", field, earlier, problems);
        }

        var stepKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!StepKeyPattern.IsMatch(step.Key ?? string.Empty))
            {
                problems.Add($"{Where()}: step key '{step.Key}' must be lowercase words joined by hyphens");
            }
            else if (!stepKeys.Add(step.Key!))
            {
                problems.Add($"{Where()}: step key '{step.Key}' is used more than once");
            }

            if (string.IsNullOrWhiteSpace(step.Name))
            {
                problems.Add($"{Where()}: step '{step.Key}' needs a name");
            }

            ValidateActor(file, step, problems);

            var capture = step.CaptureFields ?? Array.Empty<FieldDefinition>();
            if (capture.Count > 0 && step.Type != StepType.Task)
            {
                problems.Add($"{Where()}: step '{step.Key}' is not a task step, so it cannot have capture fields");
            }
            if (step.RequiresDocument && step.Type != StepType.Task)
            {
                problems.Add($"{Where()}: step '{step.Key}' is not a task step, so it cannot require a document");
            }
            var captureKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in capture)
            {
                var earlierCapture = captureKeys.ToList();
                if (!string.IsNullOrEmpty(field.Key) && !captureKeys.Add(field.Key))
                {
                    problems.Add($"{Where()}: step '{step.Key}' capture field key '{field.Key}' is used more than once");
                }
                ValidateField(file, $"step '{step.Key}' capture field '{field.Key}'", field, lookupKinds, problems);
                ValidateShowWhen(file, $"step '{step.Key}' capture field '{field.Key}'", field, earlierCapture, problems);
            }

            if (step.Condition is not null)
            {
                ValidateRule(file, def, i, step.Condition, limits, problems);
            }
        }

        var limitKeys = new HashSet<(string, string)>();
        foreach (var limit in limits)
        {
            if (!stepKeys.Contains(limit.StepKey ?? string.Empty))
            {
                problems.Add($"{Where()}: limit '{limit.LimitKey}' names unknown step '{limit.StepKey}'");
            }
            if (string.IsNullOrWhiteSpace(limit.LimitKey))
            {
                problems.Add($"{Where()}: a limit on step '{limit.StepKey}' has no limitKey");
            }
            else if (!limitKeys.Add((limit.StepKey ?? string.Empty, limit.LimitKey)))
            {
                problems.Add($"{Where()}: limit '{limit.LimitKey}' on step '{limit.StepKey}' is declared more than once");
            }
            if (limit.ValueMinor < 0)
            {
                problems.Add($"{Where()}: limit '{limit.LimitKey}' must not be negative");
            }
            if (!Units.Contains(limit.Unit))
            {
                problems.Add($"{Where()}: limit '{limit.LimitKey}' unit '{limit.Unit}' must be INR or count");
            }
        }

        if (!string.IsNullOrEmpty(def.Subject))
        {
            foreach (Match match in PlaceholderPattern.Matches(def.Subject))
            {
                var name = match.Groups[1].Value;
                if (!fieldKeys.Contains(name))
                {
                    problems.Add($"{Where()}: subject placeholder '{{{name}}}' does not match a field key (known fields: {Join(fieldKeys)})");
                }
            }
        }
    }

    private static void ValidateField(
        string file, string subject, FieldDefinition field, HashSet<string> lookupKinds, List<string> problems)
    {
        if (!FieldKeyPattern.IsMatch(field.Key ?? string.Empty))
        {
            problems.Add($"Definition '{file}': {subject} has an invalid key (use letters and digits, starting with a lowercase letter)");
        }
        if (string.IsNullOrWhiteSpace(field.Label))
        {
            problems.Add($"Definition '{file}': {subject} needs a label");
        }
        if (field.Type is FieldType.Select or FieldType.MultiSelect)
        {
            var options = field.Options ?? Array.Empty<FieldOption>();
            if (options.Count == 0)
            {
                problems.Add($"Definition '{file}': {subject} is a {field.Type} field and needs at least one option");
            }
            else if (options.Select(o => o.Value).Distinct().Count() != options.Count || options.Any(o => string.IsNullOrWhiteSpace(o.Value)))
            {
                problems.Add($"Definition '{file}': {subject} has an empty or repeated option value");
            }
        }
        if (field.Type == FieldType.Lookup)
        {
            if (string.IsNullOrWhiteSpace(field.LookupKind))
            {
                problems.Add($"Definition '{file}': {subject} is a lookup and needs a lookupKind");
            }
            else if (!lookupKinds.Contains(field.LookupKind))
            {
                problems.Add($"Definition '{file}': {subject} uses unregistered lookup kind '{field.LookupKind}' (registered: {Join(lookupKinds)})");
            }
        }
        if (field.DefaultFrom is not null && field.Type is not (FieldType.Text or FieldType.LongText))
        {
            problems.Add($"Definition '{file}': {subject} has a defaultFrom but is not a text field");
        }
        if (field.MaxLength is <= 0)
        {
            problems.Add($"Definition '{file}': {subject} maxLength must be above 0");
        }
        if (field.Min is { } min && field.Max is { } max && min > max)
        {
            problems.Add($"Definition '{file}': {subject} min is above max");
        }
    }

    // A field may only depend on a field that comes before it in the same list.
    private static void ValidateShowWhen(
        string file, string subject, FieldDefinition field, IReadOnlyList<string> earlierKeys, List<string> problems)
    {
        var rule = field.ShowWhen;
        if (rule is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(rule.Field))
        {
            problems.Add($"Definition '{file}': {subject} showWhen needs a field");
        }
        else if (rule.Field == field.Key)
        {
            problems.Add($"Definition '{file}': {subject} showWhen cannot refer to the field itself");
        }
        else if (!earlierKeys.Contains(rule.Field))
        {
            problems.Add($"Definition '{file}': {subject} showWhen refers to '{rule.Field}', which is not an earlier field in the same list (earlier fields: {Join(earlierKeys)})");
        }
        if (string.IsNullOrEmpty(rule.Value))
        {
            problems.Add($"Definition '{file}': {subject} showWhen needs a value in 'equals'");
        }
    }

    private static void ValidateActor(string file, StepDefinition step, List<string> problems)
    {
        var actor = step.Actor;
        if (actor is null)
        {
            problems.Add($"Definition '{file}': step '{step.Key}' needs an actor");
            return;
        }

        var roles = actor.Roles ?? Array.Empty<string>();
        var chosen = (actor.ReportingManager ? 1 : 0) + (actor.Requester ? 1 : 0) + (roles.Count > 0 ? 1 : 0);
        if (chosen != 1)
        {
            problems.Add($"Definition '{file}': step '{step.Key}' actor must be exactly one of reportingManager, requester or a list of roles");
            return;
        }
        foreach (var role in roles)
        {
            if (!Roles.Assignable.Contains(role))
            {
                problems.Add($"Definition '{file}': step '{step.Key}' names role '{role}' which cannot be assigned (allowed: {string.Join(", ", Roles.Assignable)})");
            }
        }
    }

    private static void ValidateRule(
        string file, ModuleDefinition def, int stepIndex, RuleNode node,
        IReadOnlyList<LimitDefinition> limits, List<string> problems)
    {
        var step = def.Steps[stepIndex];
        var prefix = $"Definition '{file}': step '{step.Key}' condition";

        var isAny = node.Any is not null;
        var isAll = node.All is not null;
        var isLeaf = node.Field is not null || node.Op is not null || node.Value is not null || node.Limit is not null;
        if ((isAny ? 1 : 0) + (isAll ? 1 : 0) + (isLeaf ? 1 : 0) != 1)
        {
            problems.Add($"{prefix} must be exactly one of any, all or a single rule");
            return;
        }

        if (isAny || isAll)
        {
            var children = isAny ? node.Any! : node.All!;
            if (children.Count == 0)
            {
                problems.Add($"{prefix} has an empty group");
            }
            foreach (var child in children)
            {
                ValidateRule(file, def, stepIndex, child, limits, problems);
            }
            return;
        }

        ValidateLeaf(file, def, stepIndex, node, limits, problems, prefix);
    }

    private static void ValidateLeaf(
        string file, ModuleDefinition def, int stepIndex, RuleNode node,
        IReadOnlyList<LimitDefinition> limits, List<string> problems, string prefix)
    {
        var step = def.Steps[stepIndex];

        if (string.IsNullOrWhiteSpace(node.Field))
        {
            problems.Add($"{prefix} has a rule without a field");
        }
        else
        {
            CheckRuleField(def, stepIndex, node.Field, problems, prefix);
        }

        if (node.Op is not { } op)
        {
            problems.Add($"{prefix} has a rule without an operator");
            return;
        }

        var hasValue = node.Value is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
        var hasLimit = !string.IsNullOrEmpty(node.Limit);

        if (hasLimit)
        {
            var exists = limits.Any(l => l.StepKey == step.Key && l.LimitKey == node.Limit);
            if (!exists)
            {
                problems.Add($"{prefix} references limit '{node.Limit}' which is not declared for this step");
            }
        }

        switch (op)
        {
            case RuleOperator.IsEmpty:
            case RuleOperator.IsNotEmpty:
                if (hasValue || hasLimit)
                {
                    problems.Add($"{prefix} operator {op} takes no value");
                }
                break;
            case RuleOperator.In:
            case RuleOperator.NotIn:
                if (hasLimit || node.Value is not { ValueKind: JsonValueKind.Array })
                {
                    problems.Add($"{prefix} operator {op} needs a list value");
                }
                break;
            case RuleOperator.Gt:
            case RuleOperator.Gte:
            case RuleOperator.Lt:
            case RuleOperator.Lte:
                if (hasValue == hasLimit)
                {
                    problems.Add($"{prefix} operator {op} needs either a number value or a limit");
                }
                else if (hasValue && node.Value is not { ValueKind: JsonValueKind.Number })
                {
                    problems.Add($"{prefix} operator {op} needs a number value");
                }
                break;
            default:
                if (hasValue == hasLimit)
                {
                    problems.Add($"{prefix} operator {op} needs either a value or a limit");
                }
                break;
        }
    }

    private static void CheckRuleField(ModuleDefinition def, int stepIndex, string field, List<string> problems, string prefix)
    {
        var fieldKeys = (def.Fields ?? Array.Empty<FieldDefinition>()).Select(f => f.Key).ToList();
        if (fieldKeys.Contains(field))
        {
            return;
        }

        if (CommonFieldNames.Contains(field))
        {
            problems.Add($"{prefix} references common field '{field}', which rules cannot read (known fields: {Join(KnownRuleFields(def, stepIndex))})");
            return;
        }

        var dot = field.IndexOf('.');
        if (dot > 0)
        {
            var stepKey = field[..dot];
            var captureKey = field[(dot + 1)..];
            var target = def.Steps.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.Key == stepKey);
            if (target.s is null)
            {
                problems.Add($"{prefix} references unknown step '{stepKey}' in '{field}' (known fields: {Join(KnownRuleFields(def, stepIndex))})");
            }
            else if (target.i >= stepIndex)
            {
                problems.Add($"{prefix} references '{field}' but step '{stepKey}' does not come before this step (known fields: {Join(KnownRuleFields(def, stepIndex))})");
            }
            else if (!(target.s.CaptureFields ?? Array.Empty<FieldDefinition>()).Any(c => c.Key == captureKey))
            {
                problems.Add($"{prefix} references unknown capture field '{captureKey}' of step '{stepKey}' (known fields: {Join(KnownRuleFields(def, stepIndex))})");
            }
            return;
        }

        problems.Add($"{prefix} references unknown field '{field}' (known fields: {Join(KnownRuleFields(def, stepIndex))})");
    }

    private static IEnumerable<string> KnownRuleFields(ModuleDefinition def, int stepIndex)
    {
        foreach (var field in def.Fields ?? Array.Empty<FieldDefinition>())
        {
            yield return field.Key;
        }
        for (var i = 0; i < stepIndex; i++)
        {
            foreach (var capture in def.Steps[i].CaptureFields ?? Array.Empty<FieldDefinition>())
            {
                yield return $"{def.Steps[i].Key}.{capture.Key}";
            }
        }
    }

    private static string Join(IEnumerable<string> values) => string.Join(", ", values);
}
