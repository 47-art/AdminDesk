import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { ApprovalStatus, RequestDetail } from '../../core/api/models';
import { APPROVAL_STATUS_DOTS, REQUEST_STATUSES } from '../../core/constants/statuses';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';
import { ApprovalTimelineComponent } from './approval-timeline.component';

export const APPROVAL_STATUS_LABELS: Record<ApprovalStatus, string> = {
  Pending: 'Pending',
  Approved: 'Approved',
  Rejected: 'Rejected',
};

/** Right-hand panel: status, approval status, where the request is now, and the timeline. */
@Component({
  selector: 'app-status-panel',
  imports: [StatusBadgeComponent, ApprovalTimelineComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .panel {
      background: #ffffff;
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      padding: var(--space-lg);
      display: flex;
      flex-direction: column;
      gap: var(--space-md);
    }
    .row {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
    }
    .label {
      color: var(--p-text-muted-color);
      font-size: 13px;
    }
    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      display: inline-block;
    }
    .secondary {
      color: var(--p-text-muted-color);
      font-size: 13px;
    }
    .current {
      font-weight: 600;
    }
  `,
  template: `
    <div class="panel">
      <app-status-badge [status]="detail().currentStatus" size="large" />
      <div class="row">
        <span class="label">Approval status</span>
        <span class="dot" [style.background]="dotColour()" aria-hidden="true"></span>
        <span>{{ approvalLabel() }}</span>
      </div>
      @if (inProgress()) {
        <div>
          <div class="current">Current status: {{ detail().currentStepName ?? 'In progress' }}</div>
          @if (withWhom()) {
            <div class="secondary">With {{ withWhom() }}</div>
          }
          <div class="secondary">Age {{ detail().ageDays }} {{ detail().ageDays === 1 ? 'day' : 'days' }}</div>
        </div>
      }
      <app-approval-timeline [detail]="detail()" />
      <ng-content />
    </div>
  `,
})
export class StatusPanelComponent {
  readonly detail = input.required<RequestDetail>();

  protected readonly inProgress = computed(() => this.detail().currentStatus === REQUEST_STATUSES.InProgress);
  protected readonly approvalLabel = computed(() => APPROVAL_STATUS_LABELS[this.detail().approvalStatus]);
  protected readonly dotColour = computed(() => APPROVAL_STATUS_DOTS[this.detail().approvalStatus]);
  protected readonly withWhom = computed(() => {
    const r = this.detail().responsible;
    return r?.name ?? r?.role ?? null;
  });
}
