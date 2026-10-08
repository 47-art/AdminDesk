import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { MenuItem } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Menu } from 'primeng/menu';

import { ApprovalStatus, RequestAction, RequestDetail } from '../../core/api/models';
import { APPROVAL_STATUS_DOTS, REQUEST_STATUSES } from '../../core/constants/statuses';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';
import { ApprovalTimelineComponent } from './approval-timeline.component';

export const APPROVAL_STATUS_LABELS: Record<ApprovalStatus, string> = {
  Pending: 'Pending',
  Approved: 'Approved',
  Rejected: 'Rejected',
};

const ACTION_ORDER: readonly RequestAction[] = ['Approve', 'Complete', 'Reject', 'Cancel'];

/** Right-hand panel: status, approval status, where the request is now, and the timeline. */
@Component({
  selector: 'app-status-panel',
  imports: [StatusBadgeComponent, ApprovalTimelineComponent, ButtonDirective, Menu],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .panel {
      background: var(--p-surface-0, #ffffff);
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
    .actions {
      display: flex;
      flex-direction: column;
      gap: var(--space-sm);
    }
    .actions button {
      width: 100%;
      height: 40px;
    }
    .none {
      color: var(--p-text-muted-color);
      margin: 0;
    }
    .dock {
      position: fixed;
      left: 0;
      right: 0;
      bottom: 0;
      z-index: 20;
      display: flex;
      flex-direction: row;
      gap: var(--space-sm);
      padding: var(--space-sm) var(--space-md);
      padding-bottom: calc(var(--space-sm) + env(safe-area-inset-bottom, 0px));
      background: var(--p-surface-0, #ffffff);
      border-top: 1px solid var(--p-content-border-color);
    }
    .dock button {
      height: 48px;
    }
    .dock .primary {
      flex: 1 1 auto;
    }
    .dock .rest {
      flex: 0 0 auto;
      width: auto;
    }
    .spacer {
      height: 80px;
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
      @if (phone()) {
        <div class="spacer" aria-hidden="true"></div>
      }
      @if (actions().length === 0) {
        <p class="none">No action needed from you</p>
      } @else if (phone()) {
        <div class="dock" role="group" aria-label="Actions">
          @for (a of dockButtons(); track a) {
            <button
              pButton
              type="button"
              [class.primary]="$first"
              [class.rest]="!$first"
              [severity]="severityOf(a)"
              [outlined]="a === 'Cancel'"
              [disabled]="busy()"
              (click)="action.emit(a)"
            >
              {{ labelOf(a) }}
            </button>
          }
          @if (menuItems().length > 0) {
            <button
              pButton
              type="button"
              class="rest"
              severity="secondary"
              [outlined]="true"
              aria-haspopup="true"
              (click)="menu.toggle($event)"
            >
              More
            </button>
            <p-menu #menu [popup]="true" [model]="menuItems()" appendTo="body" />
          }
        </div>
      } @else {
        <div class="actions" role="group" aria-label="Actions">
          @for (a of actions(); track a) {
            <button
              pButton
              type="button"
              [severity]="severityOf(a)"
              [outlined]="a === 'Cancel'"
              [disabled]="busy()"
              (click)="action.emit(a)"
            >
              {{ labelOf(a) }}
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class StatusPanelComponent {
  readonly detail = input.required<RequestDetail>();
  /** True while an action call is running. */
  readonly busy = input(false);
  readonly action = output<RequestAction>();

  protected readonly phone = signal(false);

  protected readonly inProgress = computed(() => this.detail().currentStatus === REQUEST_STATUSES.InProgress);
  protected readonly approvalLabel = computed(() => APPROVAL_STATUS_LABELS[this.detail().approvalStatus]);
  protected readonly dotColour = computed(() => APPROVAL_STATUS_DOTS[this.detail().approvalStatus]);
  protected readonly withWhom = computed(() => {
    const r = this.detail().responsible;
    return r?.name ?? r?.role ?? null;
  });

  /** Exactly the actions the server allows, in a fixed display order. */
  protected readonly actions = computed<RequestAction[]>(() => {
    const allowed = this.detail().allowedActions;
    return ACTION_ORDER.filter((a) => allowed.includes(a));
  });

  protected readonly dockButtons = computed<RequestAction[]>(() => {
    const list = this.actions();
    return list.length > 2 ? list.slice(0, 1) : list;
  });

  protected readonly menuItems = computed<MenuItem[]>(() => {
    const list = this.actions();
    if (list.length <= 2) return [];
    return list.slice(1).map((a) => ({ label: this.labelOf(a), command: () => this.action.emit(a) }));
  });

  constructor() {
    const query = window.matchMedia('(max-width: 767px)');
    this.phone.set(query.matches);
    const listener = (e: MediaQueryListEvent): void => this.phone.set(e.matches);
    query.addEventListener('change', listener);
    inject(DestroyRef).onDestroy(() => query.removeEventListener('change', listener));
  }

  protected labelOf(a: RequestAction): string {
    switch (a) {
      case 'Complete':
        return this.detail().primaryActionLabel ?? 'Complete';
      case 'Cancel':
        return 'Cancel request';
      default:
        return a;
    }
  }

  protected severityOf(a: RequestAction): 'primary' | 'danger' | 'secondary' {
    if (a === 'Reject') return 'danger';
    if (a === 'Cancel') return 'secondary';
    return 'primary';
  }
}
