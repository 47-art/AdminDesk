import { FieldDto } from '../../core/api/models';
import { FIELD_TYPES } from '../../core/constants/field-types';
import { formatDate, formatDateTime } from '../formatters/dates';
import { formatInr } from '../formatters/money';

export const NOT_PROVIDED = 'Not provided';

function isEmpty(value: unknown): boolean {
  return value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0);
}

/** True when a value would be shown as "Not provided". */
export function isBlankValue(value: unknown): boolean {
  return isEmpty(value);
}

/**
 * One value shown as text, by field type. Lookups show the label passed in `lookupLabel`;
 * multi selects show the option labels joined with commas. Empty values show "Not provided".
 */
export function formatFieldValue(field: FieldDto, value: unknown, lookupLabel?: string | null): string {
  if (field.type === FIELD_TYPES.YesNo) {
    if (value === null || value === undefined) return NOT_PROVIDED;
    return value === true ? 'Yes' : 'No';
  }
  if (isEmpty(value)) return NOT_PROVIDED;

  const optionLabel = (v: unknown): string => field.options.find((o) => o.value === String(v))?.label ?? String(v);

  switch (field.type) {
    case FIELD_TYPES.Money: {
      const n = Number(value);
      return Number.isFinite(n) ? formatInr(n) : String(value);
    }
    case FIELD_TYPES.Date: {
      const text = String(value);
      const day = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text);
      return day ? `${day[3]}/${day[2]}/${day[1]}` : formatDate(text);
    }
    case FIELD_TYPES.DateTime:
      return formatDateTime(String(value));
    case FIELD_TYPES.Select:
      return optionLabel(value);
    case FIELD_TYPES.MultiSelect:
      return (Array.isArray(value) ? value : [value]).map(optionLabel).join(', ');
    case FIELD_TYPES.Lookup:
      return lookupLabel && lookupLabel.length > 0 ? lookupLabel : String(value);
    default:
      return String(value);
  }
}
