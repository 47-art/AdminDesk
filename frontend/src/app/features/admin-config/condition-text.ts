import { ConditionFieldChoice, ConfigStep, RuleNode } from '../../core/api/admin-config.api';
import { FIELD_TYPES } from '../../core/constants/field-types';
import { formatInr } from '../../shared/formatters/money';

export const OPERATOR_WORDS: Record<string, string> = {
  eq: 'is',
  neq: 'is not',
  gt: 'is above',
  gte: 'is at least',
  lt: 'is below',
  lte: 'is at most',
  in: 'is one of',
  notIn: 'is none of',
  isEmpty: 'is empty',
  isNotEmpty: 'is not empty',
};

export const NO_OPERAND_OPERATORS: readonly string[] = ['isEmpty', 'isNotEmpty'];
export const LIST_OPERATORS: readonly string[] = ['in', 'notIn'];

/** "managerLimit" or "manager_limit" becomes "Manager limit". */
export function limitLabel(key: string): string {
  const words = key
    .replace(/[_-]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .toLowerCase()
    .trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function valueText(choice: ConditionFieldChoice | undefined, value: unknown): string {
  if (Array.isArray(value)) {
    return value.map((v) => valueText(choice, v)).join(', ');
  }
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (choice?.type === FIELD_TYPES.Money && typeof value === 'number') return formatInr(value);
  if (choice) {
    const option = choice.options.find((o) => o.value === String(value));
    if (option) return option.label;
  }
  return value === null || value === undefined ? '' : String(value);
}

function ruleText(node: RuleNode, choices: ConditionFieldChoice[]): string {
  if (node.any && node.any.length > 0) {
    return 'any of (' + node.any.map((n) => ruleText(n, choices)).join(' or ') + ')';
  }
  if (node.all && node.all.length > 0) {
    return 'all of (' + node.all.map((n) => ruleText(n, choices)).join(' and ') + ')';
  }
  const choice = choices.find((c) => c.key === node.field);
  const field = choice?.label ?? node.field ?? 'a field';
  const op = OPERATOR_WORDS[node.op ?? ''] ?? node.op ?? '';
  const operand = node.limit ? `the ${limitLabel(node.limit).toLowerCase()}` : valueText(choice, node.value);
  return `${field} ${op} ${operand}`.trim();
}

/** The step's condition as a sentence, or "Always required" when it has none. */
export function conditionSentence(step: ConfigStep): string {
  if (!step.condition) return 'Always required';
  return 'Required when ' + ruleText(step.condition, step.conditionFields);
}
