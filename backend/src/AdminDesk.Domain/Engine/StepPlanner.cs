using System.Text.Json;
using AdminDesk.Domain.Definitions;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Money;

namespace AdminDesk.Domain.Engine;

// Decides which steps are required and which one is active. Conditional steps stay
// undecided (Upcoming) until they become the next step to activate. Nothing is skipped
// automatically: a required step that nobody can act on waits.
public static class StepPlanner
{
    // Every definition step in order: Pending without a condition, Upcoming with one.
    public static IReadOnlyList<PlannedStep> PlanInitial(ModuleDefinition definition)
    {
        var steps = new List<PlannedStep>(definition.Steps.Count);
        for (var i = 0; i < definition.Steps.Count; i++)
        {
            var step = definition.Steps[i];
            steps.Add(new PlannedStep(
                i + 1,
                step.Key,
                step.Name,
                step.Type,
                step.Condition is null ? StepState.Pending : StepState.Upcoming,
                Array.Empty<ActorSlot>()));
        }
        return steps;
    }

    // Walks from the first step that is not finished. An undecided step is decided now from
    // the payload, the values captured so far and the limits; the walk stops at the first
    // step that is Pending.
    public static ForwardResult ResolveForward(
        ModuleDefinition definition,
        IReadOnlyList<PlannedStep> steps,
        FieldValues values,
        LimitSet limits)
    {
        var result = steps.ToList();
        int? active = null;

        for (var i = 0; i < result.Count; i++)
        {
            var step = result[i];
            if (step.State is StepState.Done or StepState.NotRequired)
            {
                continue;
            }
            if (step.State == StepState.Rejected)
            {
                break;
            }
            if (step.State == StepState.Upcoming)
            {
                var definitionStep = definition.Steps.First(s => s.Key == step.Key);
                var condition = NormaliseRule(definition, definitionStep.Condition);
                var required = RuleEvaluator.Evaluate(condition, values, limits, step.Key);
                step = step with { State = required ? StepState.Pending : StepState.NotRequired };
                result[i] = step;
                if (!required)
                {
                    continue;
                }
            }
            active = step.Seq;
            break;
        }

        return new ForwardResult(result, active);
    }

    public static bool IsFinished(IReadOnlyList<PlannedStep> steps) =>
        !steps.Any(s => s.State is StepState.Pending or StepState.Upcoming);

    // Rule values written against a money field are rupee literals in the definition file;
    // they are converted once to paise so the evaluator only ever sees minor units.
    public static RuleNode? NormaliseRule(ModuleDefinition definition, RuleNode? rule)
    {
        if (rule is null)
        {
            return null;
        }
        if (rule.All is not null || rule.Any is not null)
        {
            return rule with
            {
                All = rule.All?.Select(r => NormaliseRule(definition, r)!).ToList(),
                Any = rule.Any?.Select(r => NormaliseRule(definition, r)!).ToList()
            };
        }
        if (rule.Field is null || rule.Value is not { } value || !IsMoneyField(definition, rule.Field))
        {
            return rule;
        }
        return rule with { Value = ToMinorElement(value) };
    }

    private static bool IsMoneyField(ModuleDefinition definition, string field)
    {
        var dot = field.IndexOf('.');
        if (dot < 0)
        {
            return definition.Fields.Any(f => f.Key == field && f.Type == FieldType.Money);
        }
        var stepKey = field[..dot];
        var fieldKey = field[(dot + 1)..];
        var step = definition.Steps.FirstOrDefault(s => s.Key == stepKey);
        return step?.CaptureFields?.Any(f => f.Key == fieldKey && f.Type == FieldType.Money) == true;
    }

    private static JsonElement ToMinorElement(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var rupees))
        {
            return JsonSerializer.SerializeToElement(MoneyConverter.ToMinor(rupees));
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var converted = value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var r)
                    ? (object)MoneyConverter.ToMinor(r)
                    : item)
                .ToList();
            return JsonSerializer.SerializeToElement(converted);
        }
        return value;
    }
}
