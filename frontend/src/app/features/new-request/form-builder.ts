import { AbstractControl, FormControl, FormGroup, ValidatorFn, Validators } from '@angular/forms';

import { CommonFieldsInput, FieldDto, FieldError } from '../../core/api/models';
import { FIELD_TYPES, PRIORITIES, Priority } from '../../core/constants/field-types';
import { toDateOnlyString } from '../../shared/formatters/dates';

export type FieldGroup = FormGroup<Record<string, FormControl<unknown>>>;

export interface CommonGroupControls {
  projectId: FormControl<number | null>;
  locationId: FormControl<number | null>;
  costCentreId: FormControl<number | null>;
  requiredDate: FormControl<Date | null>;
  priority: FormControl<Priority | null>;
  remarks: FormControl<string | null>;
}
export type CommonGroup = FormGroup<CommonGroupControls>;

export const REMARKS_MAX_LENGTH = 1000;

/** Element id of the text box for a definition field. */
export const fieldDomId = (key: string): string => `field-${key}`;
/** Element id of the text box for a common field. */
export const commonDomId = (name: string): string => `common-${name}`;

/** Element id to focus for a control name returned by the server. */
export function domIdFor(name: string, group: FieldGroup): string {
  return Object.keys(group.controls).some((k) => k.toLowerCase() === name.toLowerCase())
    ? fieldDomId(Object.keys(group.controls).find((k) => k.toLowerCase() === name.toLowerCase()) ?? name)
    : commonDomId(name);
}

function initialValue(field: FieldDto): unknown {
  switch (field.type) {
    case FIELD_TYPES.YesNo:
      return false;
    case FIELD_TYPES.MultiSelect:
      return [];
    case FIELD_TYPES.Text:
    case FIELD_TYPES.LongText:
      return '';
    default:
      return null;
  }
}

function validatorsFor(field: FieldDto): ValidatorFn[] {
  const list: ValidatorFn[] = [];
  // A yes/no switch always holds an answer, so "required" adds nothing there.
  if (field.required && field.type !== FIELD_TYPES.YesNo) list.push(Validators.required);
  if (field.maxLength !== null && field.maxLength !== undefined) list.push(Validators.maxLength(field.maxLength));
  if (field.min !== null && field.min !== undefined) list.push(Validators.min(field.min));
  if (field.max !== null && field.max !== undefined) list.push(Validators.max(field.max));
  return list;
}

/** Same comparison the server makes: trimmed text, ignoring case. A missing answer never matches. */
function answerEquals(value: unknown, expected: string): boolean {
  if (value === null || value === undefined || Array.isArray(value) || value instanceof Date) return false;
  return String(value).trim().toLowerCase() === String(expected).trim().toLowerCase();
}

/**
 * Enables or disables each field that has a show-when rule. A hidden field is disabled (so it is not
 * validated and not sent) and its value is cleared. A field is also hidden while the field it
 * depends on is hidden.
 */
export function applyShowWhen(group: FieldGroup, fields: FieldDto[]): void {
  const shown = new Map<string, boolean>();
  for (const field of fields) {
    const control = group.controls[field.key];
    const rule = field.showWhen;
    if (!control || !rule) {
      shown.set(field.key, true);
      continue;
    }
    const other = group.controls[rule.field];
    const visible = !!other && shown.get(rule.field) !== false && answerEquals(other.value, rule.equals);
    shown.set(field.key, visible);
    if (visible && control.disabled) {
      control.enable({ emitEvent: false });
    } else if (!visible && control.enabled) {
      control.reset(initialValue(field), { emitEvent: false });
      control.disable({ emitEvent: false });
    }
  }
}

/** One typed control per field, keyed by the field key, with the validators the definition asks for. */
export function buildGroup(fields: FieldDto[]): FieldGroup {
  const controls: Record<string, FormControl<unknown>> = {};
  for (const field of fields) {
    controls[field.key] = new FormControl<unknown>(initialValue(field), validatorsFor(field));
  }
  const group = new FormGroup(controls);
  if (fields.some((f) => f.showWhen)) {
    applyShowWhen(group, fields);
    group.valueChanges.subscribe(() => applyShowWhen(group, fields));
  }
  return group;
}

/** The fixed fields every request carries. */
export function buildCommonGroup(): CommonGroup {
  return new FormGroup<CommonGroupControls>({
    projectId: new FormControl<number | null>(null),
    locationId: new FormControl<number | null>(null),
    costCentreId: new FormControl<number | null>(null),
    requiredDate: new FormControl<Date | null>(null),
    priority: new FormControl<Priority | null>(PRIORITIES.Medium),
    remarks: new FormControl<string | null>(null, [Validators.maxLength(REMARKS_MAX_LENGTH)]),
  });
}

/** A date-time as an instant for the wire. Only date-time fields use this; calendar days never do. */
export const toDateTimeInstant = (value: Date): string => value.toISOString();

function toWire(field: FieldDto, value: unknown): unknown {
  switch (field.type) {
    case FIELD_TYPES.Text:
    case FIELD_TYPES.LongText: {
      const text = typeof value === 'string' ? value.trim() : '';
      return text === '' ? null : text;
    }
    case FIELD_TYPES.Number:
    case FIELD_TYPES.Money:
      return typeof value === 'number' && Number.isFinite(value) ? value : null;
    case FIELD_TYPES.Date:
      return value instanceof Date ? toDateOnlyString(value) : null;
    case FIELD_TYPES.DateTime:
      return value instanceof Date ? toDateTimeInstant(value) : null;
    case FIELD_TYPES.YesNo:
      return value === true;
    case FIELD_TYPES.MultiSelect:
      return Array.isArray(value) ? value : [];
    case FIELD_TYPES.Lookup:
      return typeof value === 'number' ? value : null;
    default:
      return value === undefined || value === '' ? null : value;
  }
}

/** Values keyed by field key, in the form the API expects. */
export function toPayload(group: FieldGroup, fields: FieldDto[]): Record<string, unknown> {
  const payload: Record<string, unknown> = {};
  for (const field of fields) {
    // A field hidden by its show-when rule is not sent.
    if (field.showWhen && group.controls[field.key]?.disabled) continue;
    payload[field.key] = toWire(field, group.controls[field.key]?.value);
  }
  return payload;
}

export function toCommon(common: CommonGroup): CommonFieldsInput {
  const v = common.getRawValue();
  const remarks = v.remarks?.trim() ?? '';
  return {
    projectId: v.projectId,
    locationId: v.locationId,
    costCentreId: v.costCentreId,
    requiredDate: v.requiredDate ? toDateOnlyString(v.requiredDate) : null,
    priority: v.priority,
    remarks: remarks === '' ? null : remarks,
  };
}

function findControl(name: string, group: FieldGroup, common: CommonGroup | null): FormControl<unknown> | null {
  const bare = name.replace(/^(payload|common)\./i, '');
  const lower = bare.toLowerCase();
  for (const [key, control] of Object.entries(group.controls)) {
    if (key.toLowerCase() === lower) return control;
  }
  if (common) {
    for (const [key, control] of Object.entries(common.controls)) {
      if (key.toLowerCase() === lower) return control as FormControl<unknown>;
    }
  }
  return null;
}

/**
 * Puts each server message on the control it names. Returns the name of the first control that
 * received one (for focus) and the messages that matched no control.
 */
export function applyServerErrors(
  group: FieldGroup,
  common: CommonGroup | null,
  fieldErrors: FieldError[],
): { firstInvalid: string | null; unmatched: FieldError[]; count: number } {
  let firstInvalid: string | null = null;
  const unmatched: FieldError[] = [];
  let count = 0;
  for (const error of fieldErrors) {
    const control = findControl(error.field, group, common);
    if (!control) {
      unmatched.push(error);
      continue;
    }
    control.setErrors({ server: error.message });
    control.markAsTouched();
    count++;
    firstInvalid ??= error.field.replace(/^(payload|common)\./i, '');
  }
  return { firstInvalid, unmatched, count };
}

/** Message to show under a control, or null when it is valid or untouched. */
export function errorMessage(control: AbstractControl | null | undefined, field?: FieldDto): string | null {
  if (!control || !control.invalid || !(control.touched || control.dirty)) return null;
  const errors = control.errors;
  if (!errors) return null;
  if (typeof errors['server'] === 'string') return errors['server'];
  if (errors['required']) return 'Enter a value for this field.';
  if (errors['maxlength']) return `Use ${errors['maxlength'].requiredLength} characters or fewer.`;
  if (errors['min']) return `Enter a value of at least ${errors['min'].min}.`;
  if (errors['max']) return `Enter a value of at most ${errors['max'].max}.`;
  return field ? `Check the value for ${field.label}.` : 'Check this value.';
}
