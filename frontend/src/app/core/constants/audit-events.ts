/** Audit event types written by the server, with the wording shown in the audit trail. */
export const AUDIT_EVENT_LABELS: Record<string, string> = {
  Created: 'Created',
  StepApproved: 'Approved',
  StepCompleted: 'Completed',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
  Closed: 'Closed',
  StepActivated: 'Step started',
  StepSkipped: 'Step skipped',
  DefinitionSynced: 'Definition synced',
};

/** Events that move the request to a new status; only these rows show From and To. */
export const STATUS_CHANGING_EVENTS: readonly string[] = ['Created', 'Closed', 'Rejected', 'Cancelled'];
