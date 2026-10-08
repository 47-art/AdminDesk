import { ChangeDetectionStrategy, Component, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Drawer } from 'primeng/drawer';
import { TableModule } from 'primeng/table';

import { AuditEventDto } from '../../core/api/models';
import { RequestsApi } from '../../core/api/requests.api';
import { AUDIT_EVENT_LABELS, STATUS_CHANGING_EVENTS } from '../../core/constants/audit-events';
import { REQUEST_STATUS_STYLES, RequestStatus } from '../../core/constants/statuses';
import { formatDateTime } from '../../shared/formatters/dates';
import { PageSkeletonComponent } from '../../shared/page-skeleton/page-skeleton.component';

/**
 * Button and right-hand drawer with the read-only event history of a request.
 * The page creates it only for the audit viewers. Events are fetched when the drawer is first opened,
 * and again on a later open only if the request has changed since.
 */
@Component({
  selector: 'app-audit-trail-drawer',
  imports: [Drawer, TableModule, ButtonDirective, PageSkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .muted {
      color: var(--p-text-muted-color);
    }
    .error {
      display: flex;
      align-items: center;
      gap: var(--space-md);
    }
    .comment {
      overflow-wrap: anywhere;
      white-space: pre-line;
    }
  `,
  template: `
    <button
      #trigger
      pButton
      type="button"
      severity="secondary"
      [outlined]="true"
      icon="pi pi-history"
      label="Audit trail"
      aria-haspopup="dialog"
      (click)="open()"
    ></button>
    <p-drawer
      [(visible)]="visible"
      (onHide)="returnFocus()"
      position="right"
      header="Audit trail"
      [modal]="true"
      [dismissible]="true"
      [closeOnEscape]="true"
      [style]="{ width: 'min(960px, 100vw)' }"
    >
      @if (loading()) {
        <app-page-skeleton preset="rows" />
      } @else if (failed()) {
        <div class="error">
          <span>We could not load the audit trail. Try again.</span>
          <button pButton type="button" severity="secondary" [outlined]="true" size="small" (click)="load()">
            Try again
          </button>
        </div>
      } @else if (events().length === 0) {
        <p class="muted">No events recorded yet</p>
      } @else {
        <p-table [value]="events()" size="small" [scrollable]="true" aria-label="Audit trail">
          <ng-template #header>
            <tr>
              <th scope="col">Time</th>
              <th scope="col">Event</th>
              <th scope="col">Actor</th>
              <th scope="col">Step</th>
              <th scope="col">From</th>
              <th scope="col">To</th>
              <th scope="col">Comment</th>
            </tr>
          </ng-template>
          <ng-template #body let-e>
            <tr>
              <td>{{ time(e) }}</td>
              <td>{{ eventLabel(e) }}</td>
              <td>{{ actor(e) }}</td>
              <td>{{ e.stepName ?? e.stepKey ?? '' }}</td>
              <td>{{ statusLabel(e, e.fromStatus) }}</td>
              <td>{{ statusLabel(e, e.toStatus) }}</td>
              <td class="comment">{{ e.comment ?? '' }}</td>
            </tr>
          </ng-template>
        </p-table>
      }
    </p-drawer>
  `,
})
export class AuditTrailDrawerComponent {
  readonly requestId = input.required<number>();
  readonly rowVersion = input.required<number>();

  private readonly api = inject(RequestsApi);
  private readonly trigger = viewChild.required<ElementRef<HTMLElement>>('trigger');

  protected readonly visible = signal(false);
  protected readonly events = signal<AuditEventDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);

  private loadedKey: string | null = null;

  protected open(): void {
    this.visible.set(true);
    if (this.loadedKey !== this.key()) this.load();
  }

  /** Runs when the drawer closes by any route (Esc, click outside, close button). */
  protected returnFocus(): void {
    this.trigger().nativeElement.focus();
  }

  protected load(): void {
    const requestId = this.requestId();
    const key = this.key();
    this.loading.set(true);
    this.failed.set(false);
    this.api.audit(requestId).subscribe({
      next: (list) => {
        if (requestId !== this.requestId()) return;
        this.events.set(list);
        this.loadedKey = key;
        this.loading.set(false);
      },
      error: () => {
        if (requestId !== this.requestId()) return;
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  private key(): string {
    return `${this.requestId()}:${this.rowVersion()}`;
  }

  protected time(e: AuditEventDto): string {
    return formatDateTime(e.createdUtc);
  }

  protected actor(e: AuditEventDto): string {
    if (!e.actorName) return 'System';
    return e.actorRole ? `${e.actorName} (${e.actorRole})` : e.actorName;
  }

  protected eventLabel(e: AuditEventDto): string {
    return AUDIT_EVENT_LABELS[e.eventType] ?? e.eventType;
  }

  /** From and To only mean something on events that change the request status. */
  protected statusLabel(e: AuditEventDto, status: string | null): string {
    if (!status || !STATUS_CHANGING_EVENTS.includes(e.eventType)) return '';
    return REQUEST_STATUS_STYLES[status as RequestStatus]?.label ?? status;
  }
}
