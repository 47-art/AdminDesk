import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { Timeline } from 'primeng/timeline';

import { FieldDto, RequestDetail, RequestStepDto } from '../../core/api/models';
import { STEP_TYPES } from '../../core/constants/field-types';
import {
  REPORTING_MANAGER_LABEL,
  REQUEST_STATUS_STYLES,
  STEP_STATES,
  STEP_STATE_STYLES,
  STEP_LATER_STYLE,
  StatusStyle,
} from '../../core/constants/statuses';
import { formatDateTime } from '../../shared/formatters/dates';
import { formatFieldValue } from '../../shared/field-value/format-field-value';

interface CapturedLine {
  label: string;
  value: string;
}

interface Entry {
  id: string;
  title: string;
  style: StatusStyle;
  current: boolean;
  /** Dashed edge for steps not yet decided. */
  dashed: boolean;
  muted: boolean;
  line: string | null;
  note: string | null;
  captured: CapturedLine[];
  secondary: string | null;
}

const COLLAPSE_AFTER_CHARS = 90;

/** Vertical list of the approval and task steps with who acted, when, and what each step carries. */
@Component({
  selector: 'app-approval-timeline',
  imports: [Timeline],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    h2 {
      margin: 0 0 var(--space-sm);
    }
    .marker {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 28px;
      height: 28px;
      border-radius: 50%;
      font-size: 13px;
      border: 1px solid transparent;
    }
    .marker.outline {
      border-color: var(--p-content-border-color);
    }
    .marker.dashed {
      border-style: dashed;
    }
    .marker.current {
      box-shadow: 0 0 0 3px var(--p-primary-color);
    }
    .entry {
      padding-bottom: var(--space-md);
      min-width: 0;
    }
    .entry.muted .title {
      color: var(--p-text-muted-color);
    }
    .title {
      font-weight: 600;
      overflow-wrap: anywhere;
    }
    .secondary {
      color: var(--p-text-muted-color);
      font-size: 13px;
    }
    .note {
      margin-top: 2px;
      font-size: 13px;
      overflow-wrap: anywhere;
      white-space: pre-line;
    }
    .note.clamped {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .toggle {
      background: none;
      border: 0;
      padding: 0;
      color: var(--p-primary-color);
      font: inherit;
      font-size: 12px;
      cursor: pointer;
    }
    .captured {
      margin: 2px 0 0;
      padding: 0;
      list-style: none;
      font-size: 13px;
      overflow-wrap: anywhere;
    }
    :host ::ng-deep .p-timeline-event-opposite {
      display: none;
    }
  `,
  template: `
    <h2 class="text-section">Timeline</h2>
    <p-timeline [value]="entries()" align="left">
      <ng-template #marker let-entry>
        <span
          class="marker"
          [class.outline]="entry.style.outline"
          [class.dashed]="entry.dashed"
          [class.current]="entry.current"
          [style.background]="entry.style.background"
          [style.color]="entry.style.text"
          aria-hidden="true"
        >
          <i class="pi" [class]="entry.style.icon"></i>
        </span>
      </ng-template>
      <ng-template #content let-entry>
        <div class="entry" [class.muted]="entry.muted">
          <div class="title">{{ entry.title }}</div>
          @if (entry.line) {
            <div class="secondary">{{ entry.line }}</div>
          }
          @if (entry.secondary) {
            <div class="secondary">{{ entry.secondary }}</div>
          }
          @if (entry.captured.length > 0) {
            <ul class="captured">
              @for (c of entry.captured; track c.label) {
                <li>{{ c.label }}: {{ c.value }}</li>
              }
            </ul>
          }
          @if (entry.note) {
            <div class="note" [class.clamped]="isClamped(entry)">{{ entry.note }}</div>
            @if (entry.note.length > ${COLLAPSE_AFTER_CHARS}) {
              <button
                type="button"
                class="toggle"
                [attr.aria-expanded]="!isClamped(entry)"
                (click)="toggle(entry.id)"
              >
                {{ isClamped(entry) ? 'Show more' : 'Show less' }}
              </button>
            }
          }
        </div>
      </ng-template>
    </p-timeline>
  `,
})
export class ApprovalTimelineComponent {
  readonly detail = input.required<RequestDetail>();

  private readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected readonly entries = computed<Entry[]>(() => {
    const d = this.detail();
    const list = d.steps.map((s) => this.toEntry(s, d));
    if (d.cancelReason) {
      list.push({
        id: 'cancelled',
        title: 'Cancelled',
        style: REQUEST_STATUS_STYLES.Cancelled,
        current: false,
        dashed: false,
        muted: false,
        line: `Cancelled by ${d.stoppedByName ?? d.requester.name}${d.stoppedByRole ? ` (${d.stoppedByRole})` : ''}${d.cancelledUtc ? ` on ${formatDateTime(d.cancelledUtc)}` : ''}`,
        note: d.cancelReason,
        captured: [],
        secondary: null,
      });
    }
    return list;
  });

  protected isClamped(entry: Entry): boolean {
    return entry.note !== null && entry.note.length > COLLAPSE_AFTER_CHARS && !this.expanded().has(entry.id);
  }

  protected toggle(id: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  // " (Admin)" after the name of whoever rejected the request, when the audit trail names the same person.
  private rejectedRole(step: RequestStepDto, d: RequestDetail): string {
    return d.stoppedByRole && d.stoppedByName === step.actedByName ? ` (${d.stoppedByRole})` : '';
  }

  private toEntry(step: RequestStepDto, d: RequestDetail): Entry {
    const base = { id: `step-${step.seq}`, title: step.name, current: step.isCurrent, note: null, captured: [] as CapturedLine[] };
    const role = step.actorLabel ?? REPORTING_MANAGER_LABEL;

    switch (step.state) {
      case STEP_STATES.Done: {
        const verb = step.type === STEP_TYPES.Approval ? 'Approved' : 'Completed';
        return {
          ...base,
          style: STEP_STATE_STYLES.Done,
          dashed: false,
          muted: false,
          line: `${verb} by ${step.actedByName ?? role}${step.actedUtc ? ` on ${formatDateTime(step.actedUtc)}` : ''}`,
          secondary: null,
          captured: this.capturedLines(step, d),
        };
      }
      case STEP_STATES.Rejected:
        return {
          ...base,
          style: STEP_STATE_STYLES.Rejected,
          dashed: false,
          muted: false,
          line: `Rejected by ${step.actedByName ?? role}${this.rejectedRole(step, d)}${step.actedUtc ? ` on ${formatDateTime(step.actedUtc)}` : ''}`,
          secondary: null,
          note: step.comment,
        };
      case STEP_STATES.NotRequired:
        return {
          ...base,
          style: STEP_STATE_STYLES.NotRequired,
          dashed: false,
          muted: true,
          line: 'Not required',
          secondary: null,
        };
      case STEP_STATES.Upcoming:
        return {
          ...base,
          style: STEP_STATE_STYLES.Upcoming,
          dashed: true,
          muted: false,
          line: null,
          secondary: 'Decided when it is reached',
        };
      default: {
        const isCurrent = step.isCurrent;
        return {
          ...base,
          style: isCurrent ? STEP_STATE_STYLES.Pending : STEP_LATER_STYLE,
          dashed: false,
          muted: false,
          line: null,
          secondary: isCurrent ? `Waiting for ${d.responsible?.name ?? d.responsible?.role ?? role}` : role,
        };
      }
    }
  }

  private capturedLines(step: RequestStepDto, d: RequestDetail): CapturedLine[] {
    const values = step.captured;
    if (!values) return [];
    return step.captureFields.map((field: FieldDto) => ({
      label: field.label,
      value: formatFieldValue(field, values[field.key], d.lookupLabels[`${step.key}.${field.key}`]),
    }));
  }
}
