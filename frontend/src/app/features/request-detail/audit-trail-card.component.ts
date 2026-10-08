import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { TableModule } from 'primeng/table';

import { AuditEventDto } from '../../core/api/models';
import { RequestsApi } from '../../core/api/requests.api';
import { REQUEST_STATUS_STYLES, RequestStatus } from '../../core/constants/statuses';
import { formatDateTime } from '../../shared/formatters/dates';
import { PageSkeletonComponent } from '../../shared/page-skeleton/page-skeleton.component';

/** Read-only event history of a request. The page creates it only for Admin and SystemAdmin. */
@Component({
  selector: 'app-audit-trail-card',
  imports: [TableModule, ButtonDirective, PageSkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .card {
      background: #ffffff;
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      padding: var(--space-lg);
    }
    h2 {
      margin: 0 0 var(--space-md);
    }
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
    <section class="card" aria-labelledby="audit-title">
      <h2 id="audit-title" class="text-heading">Audit trail</h2>
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
              <td>{{ e.eventType }}</td>
              <td>{{ actor(e) }}</td>
              <td>{{ e.stepKey ?? '' }}</td>
              <td>{{ statusLabel(e.fromStatus) }}</td>
              <td>{{ statusLabel(e.toStatus) }}</td>
              <td class="comment">{{ e.comment ?? '' }}</td>
            </tr>
          </ng-template>
        </p-table>
      }
    </section>
  `,
})
export class AuditTrailCardComponent implements OnInit {
  readonly requestId = input.required<number>();

  private readonly api = inject(RequestsApi);

  protected readonly events = signal<AuditEventDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.audit(this.requestId()).subscribe({
      next: (list) => {
        this.events.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected time(e: AuditEventDto): string {
    return formatDateTime(e.createdUtc);
  }

  protected actor(e: AuditEventDto): string {
    if (!e.actorName) return 'System';
    return e.actorRole ? `${e.actorName} (${e.actorRole})` : e.actorName;
  }

  protected statusLabel(status: string | null): string {
    if (!status) return '';
    return REQUEST_STATUS_STYLES[status as RequestStatus]?.label ?? status;
  }
}
