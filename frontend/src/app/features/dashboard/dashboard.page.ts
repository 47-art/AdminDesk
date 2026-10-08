import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';

import { DashboardSummary } from '../../core/api/models';
import { RequestsApi } from '../../core/api/requests.api';
import { ROUTE_PATHS } from '../../core/constants/routes';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { relativeTime } from '../../shared/formatters/dates';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';

interface Counter {
  label: string;
  value: number;
  link: string;
  query: Record<string, string> | null;
}

/** The same six counters and the five most recent items for every role. */
@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, ButtonDirective, Skeleton, EmptyStateComponent, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    h1 {
      margin: 0 0 var(--space-md);
    }
    .counters {
      display: grid;
      grid-template-columns: repeat(6, minmax(0, 1fr));
      gap: var(--space-md);
      margin-bottom: var(--space-xl, 32px);
    }
    @media (max-width: 1023px) {
      .counters {
        grid-template-columns: repeat(3, minmax(0, 1fr));
      }
    }
    @media (max-width: 767px) {
      .counters {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
    .counter {
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
      padding: var(--space-md);
      background: #ffffff;
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      text-decoration: none;
      color: var(--p-text-color);
      min-height: 92px;
    }
    .counter:hover {
      border-color: var(--p-primary-color);
    }
    .counter:focus-visible {
      outline: 2px solid var(--p-primary-color);
      outline-offset: 2px;
    }
    .number {
      font-size: 28px;
      font-weight: 600;
      line-height: 1.2;
    }
    .caption {
      font-size: 12px;
      color: var(--p-text-muted-color);
    }
    h2 {
      margin: 0 0 var(--space-sm);
    }
    .card {
      background: #ffffff;
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
    }
    .recent {
      list-style: none;
      margin: 0;
      padding: 0;
    }
    .recent li {
      display: grid;
      grid-template-columns: 140px 1fr auto auto;
      align-items: center;
      gap: var(--space-md);
      padding: var(--space-sm) var(--space-md);
      border-bottom: 1px solid var(--p-content-border-color);
    }
    .recent li:last-child {
      border-bottom: 0;
    }
    @media (max-width: 767px) {
      .recent li {
        grid-template-columns: 1fr auto;
      }
    }
    .link {
      color: var(--p-primary-color);
      text-decoration: none;
      font-weight: 600;
    }
    .secondary {
      color: var(--p-text-muted-color);
      font-size: 13px;
    }
    .error {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      padding: var(--space-md);
    }
  `,
  template: `
    <h1 class="text-heading">Dashboard</h1>

    @if (failed()) {
      <div class="card error" role="alert">
        <span>We could not load your dashboard. Try again.</span>
        <button pButton type="button" severity="secondary" [text]="true" (click)="load()">Try again</button>
      </div>
    } @else if (summary(); as data) {
      <div class="counters">
        @for (c of counters(data); track c.label) {
          <a class="counter" [routerLink]="c.link" [queryParams]="c.query">
            <span class="number">{{ c.value }}</span>
            <span class="caption">{{ c.label }}</span>
          </a>
        }
      </div>

      <h2 class="text-heading">Recent activity</h2>
      @if (data.recent.length === 0) {
        <app-empty-state
          icon="pi-history"
          title="No recent activity yet"
          body="Requests you raise or act on will appear here."
          actionLabel="Start a request"
          (action)="startRequest()"
        />
      } @else {
        <div class="card">
          <ul class="recent">
            @for (row of data.recent.slice(0, 5); track row.id) {
              <li>
                <a class="link" [routerLink]="['/', paths.RequestDetail, row.id]">{{ row.requestNo }}</a>
                <span>{{ row.subject }}</span>
                <app-status-badge [status]="row.currentStatus" />
                <span class="secondary" [title]="relative(row.updatedUtc).exact">{{ relative(row.updatedUtc).text }}</span>
              </li>
            }
          </ul>
        </div>
      }
    } @else {
      <div class="counters" aria-hidden="true">
        @for (n of skeletons; track n) {
          <p-skeleton width="100%" height="92px" borderRadius="8px" />
        }
      </div>
    }
  `,
})
export class DashboardPage implements OnInit {
  private readonly api = inject(RequestsApi);
  private readonly router = inject(Router);

  protected readonly paths = ROUTE_PATHS;
  protected readonly skeletons = [1, 2, 3, 4, 5, 6];
  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly failed = signal(false);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.failed.set(false);
    this.api.dashboardSummary().subscribe({
      next: (data) => this.summary.set(data),
      error: () => this.failed.set(true),
    });
  }

  protected counters(data: DashboardSummary): Counter[] {
    const mine = `/${ROUTE_PATHS.MyRequests}`;
    return [
      { label: 'Waiting for me', value: data.waitingForMe, link: `/${ROUTE_PATHS.Inbox}`, query: null },
      { label: 'Total', value: data.total, link: mine, query: null },
      { label: 'Pending', value: data.pending, link: mine, query: { status: 'InProgress', approvalStatus: 'Pending' } },
      { label: 'Approved', value: data.approved, link: mine, query: { approvalStatus: 'Approved' } },
      { label: 'Rejected', value: data.rejected, link: mine, query: { status: 'Rejected' } },
      { label: 'Completed', value: data.completed, link: mine, query: { status: 'Closed' } },
    ];
  }

  protected relative(value: string): { text: string; exact: string } {
    return relativeTime(value);
  }

  protected startRequest(): void {
    void this.router.navigate(['/', ROUTE_PATHS.NewRequest]);
  }
}
