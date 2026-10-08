using System.Text.Json;
using AdminDesk.Domain.Definitions;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Domain.Engine;

// Pure evaluation of routing rules. Money is compared as integer minor units; numbers are
// compared as decimal. Nothing here rounds or converts currency.
public static class RuleEvaluator
{
    public static bool Evaluate(RuleNode? rule, FieldValues values, LimitSet limits, string stepKey)
    {
        if (rule is null)
        {
            return true;
        }
        if (rule.All is { Count: > 0 })
        {
            return rule.All.All(child => Evaluate(child, values, limits, stepKey));
        }
        if (rule.Any is { Count: > 0 })
        {
            return rule.Any.Any(child => Evaluate(child, values, limits, stepKey));
        }
        if (rule.Field is null || rule.Op is null)
        {
            return false;
        }
        return EvaluateLeaf(rule, values, limits, stepKey);
    }

    private static bool EvaluateLeaf(RuleNode rule, FieldValues values, LimitSet limits, string stepKey)
    {
        values.TryGet(rule.Field!, out var actual);
        var op = rule.Op!.Value;

        if (op == RuleOperator.IsEmpty)
        {
            return IsEmpty(actual);
        }
        if (op == RuleOperator.IsNotEmpty)
        {
            return !IsEmpty(actual);
        }
        if (IsEmpty(actual))
        {
            return false;
        }

        if (rule.Limit is not null)
        {
            if (!limits.TryGet(stepKey, rule.Limit, out var limit) || !TryToDecimal(actual, out var number))
            {
                return false;
            }
            return CompareNumbers(op, number, limit.ValueMinor);
        }

        if (rule.Value is not { } expected)
        {
            return false;
        }

        switch (op)
        {
            case RuleOperator.In:
                return Contains(expected, actual);
            case RuleOperator.NotIn:
                return !Contains(expected, actual);
            case RuleOperator.Eq:
                return AreEqual(expected, actual);
            case RuleOperator.Neq:
                return !AreEqual(expected, actual);
            default:
                return CompareOrdered(op, expected, actual);
        }
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        string[] a => a.Length == 0,
        _ => false
    };

    private static bool TryToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case long l: result = l; return true;
            case int i: result = i; return true;
            case decimal d: result = d; return true;
            default: result = 0; return false;
        }
    }

    private static bool TryToDecimal(JsonElement element, out decimal result)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out result);
        }
        result = 0;
        return false;
    }

    private static bool CompareNumbers(RuleOperator op, decimal actual, decimal expected) => op switch
    {
        RuleOperator.Eq => actual == expected,
        RuleOperator.Neq => actual != expected,
        RuleOperator.Gt => actual > expected,
        RuleOperator.Gte => actual >= expected,
        RuleOperator.Lt => actual < expected,
        RuleOperator.Lte => actual <= expected,
        _ => false
    };

    private static bool CompareOrdered(RuleOperator op, JsonElement expected, object? actual)
    {
        if (expected.ValueKind == JsonValueKind.Number)
        {
            return TryToDecimal(actual, out var number) && TryToDecimal(expected, out var target)
                && CompareNumbers(op, number, target);
        }
        if (expected.ValueKind == JsonValueKind.String && actual is string text)
        {
            var order = string.CompareOrdinal(text, expected.GetString());
            return CompareNumbers(op, order, 0);
        }
        return false;
    }

    private static bool AreEqual(JsonElement expected, object? actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Number:
                return TryToDecimal(actual, out var number) && TryToDecimal(expected, out var target) && number == target;
            case JsonValueKind.String:
                return actual switch
                {
                    string s => string.Equals(s, expected.GetString(), StringComparison.Ordinal),
                    string[] a => a.Contains(expected.GetString(), StringComparer.Ordinal),
                    _ => false
                };
            case JsonValueKind.True:
                return actual is bool b1 && b1;
            case JsonValueKind.False:
                return actual is bool b2 && !b2;
            default:
                return false;
        }
    }

    private static bool Contains(JsonElement list, object? actual)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            return AreEqual(list, actual);
        }
        foreach (var item in list.EnumerateArray())
        {
            if (actual is string[] many)
            {
                if (many.Any(m => AreEqual(item, m)))
                {
                    return true;
                }
            }
            else if (AreEqual(item, actual))
            {
                return true;
            }
        }
        return false;
    }
}
