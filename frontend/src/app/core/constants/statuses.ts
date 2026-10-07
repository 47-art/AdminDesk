// The only place status colours, icons and labels are declared.

export const REQUEST_STATUSES = {
  InProgress: 'InProgress',
  Closed: 'Closed',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
} as const;
export type RequestStatus = (typeof REQUEST_STATUSES)[keyof typeof REQUEST_STATUSES];

export const APPROVAL_STATUSES = {
  Pending: 'Pending',
  Approved: 'Approved',
  Rejected: 'Rejected',
} as const;
export type ApprovalStatus = (typeof APPROVAL_STATUSES)[keyof typeof APPROVAL_STATUSES];

export const STEP_STATES = {
  Pending: 'Pending',
  Upcoming: 'Upcoming',
  NotRequired: 'NotRequired',
  Done: 'Done',
  Rejected: 'Rejected',
} as const;
export type StepState = (typeof STEP_STATES)[keyof typeof STEP_STATES];

export interface StatusStyle {
  label: string;
  background: string;
  text: string;
  icon: string;
  /** Dashed outline instead of a solid fill edge. */
  dashed?: boolean;
  /** Outline-only look: transparent fill with a coloured border. */
  outline?: boolean;
}

export const REQUEST_STATUS_STYLES: Record<RequestStatus, StatusStyle> = {
  InProgress: { label: 'In progress', background: '#DCE9F8', text: '#1F4E8C', icon: 'pi-sync' },
  Closed: { label: 'Closed', background: '#DDF3E4', text: '#17603A', icon: 'pi-check-circle' },
  Rejected: { label: 'Rejected', background: '#FBE0E0', text: '#9B1C1C', icon: 'pi-times' },
  Cancelled: { label: 'Cancelled', background: '#E8ECF0', text: '#455363', icon: 'pi-ban' },
};

export const STEP_STATE_STYLES: Record<StepState, StatusStyle> = {
  Done: { label: 'Done', background: '#DDF3E4', text: '#17603A', icon: 'pi-check' },
  Pending: { label: 'Pending', background: '#FDF0D0', text: '#7A4B00', icon: 'pi-clock' },
  Upcoming: {
    label: 'Decided when it is reached',
    background: '#FFFFFF',
    text: '#455363',
    icon: 'pi-question-circle',
    dashed: true,
    outline: true,
  },
  NotRequired: { label: 'Not required', background: '#E8ECF0', text: '#455363', icon: 'pi-minus' },
  Rejected: { label: 'Rejected', background: '#FBE0E0', text: '#9B1C1C', icon: 'pi-times' },
};

/** A required step that is Pending but is not the step the request is currently at. */
export const STEP_LATER_STYLE: StatusStyle = {
  label: 'Later',
  background: '#FFFFFF',
  text: '#455363',
  icon: 'pi-circle',
  outline: true,
};

/** Dot colours for the approval status text. */
export const APPROVAL_STATUS_DOTS: Record<ApprovalStatus, string> = {
  Pending: '#B7791F',
  Approved: '#17603A',
  Rejected: '#9B1C1C',
};

export const STATUS_SORT_ORDER: readonly RequestStatus[] = [
  REQUEST_STATUSES.InProgress,
  REQUEST_STATUSES.Closed,
  REQUEST_STATUSES.Rejected,
  REQUEST_STATUSES.Cancelled,
];
