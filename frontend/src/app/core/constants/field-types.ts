// Wire values mirror the API enums exactly (PascalCase).

export const FIELD_TYPES = {
  Text: 'Text',
  LongText: 'LongText',
  Number: 'Number',
  Money: 'Money',
  Date: 'Date',
  DateTime: 'DateTime',
  YesNo: 'YesNo',
  Select: 'Select',
  MultiSelect: 'MultiSelect',
  Lookup: 'Lookup',
} as const;
export type FieldType = (typeof FIELD_TYPES)[keyof typeof FIELD_TYPES];

export const STEP_TYPES = {
  Approval: 'Approval',
  Task: 'Task',
} as const;
export type StepType = (typeof STEP_TYPES)[keyof typeof STEP_TYPES];

export const PRIORITIES = {
  Low: 'Low',
  Medium: 'Medium',
  High: 'High',
  Critical: 'Critical',
} as const;
export type Priority = (typeof PRIORITIES)[keyof typeof PRIORITIES];

export const REQUEST_ACTIONS = {
  Approve: 'Approve',
  Reject: 'Reject',
  Complete: 'Complete',
  Cancel: 'Cancel',
} as const;
export type RequestAction = (typeof REQUEST_ACTIONS)[keyof typeof REQUEST_ACTIONS];

/**
 * Lookup kinds registered today. A kind is a plain string everywhere in the app
 * (never a closed type), so a newly registered kind needs no frontend type change.
 */
export const LOOKUP_KINDS: readonly string[] = ['employee', 'department', 'project', 'location', 'costCentre'];
