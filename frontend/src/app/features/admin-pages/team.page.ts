import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';

import { AdminApi } from '../../core/api/admin.api';
import { TeamRow } from '../../core/api/models';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';

const DEBOUNCE_MS = 300;
const PAGE_SIZES = [10, 20, 50];

/** Read-only list of the people the signed-in manager can see. */
@Component({
  selector: 'app-team-page',
  imports: [FormsModule, ButtonDirective, InputText, Message, Skeleton, TableModule, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    h1 {
      margin: 0 0 var(--space-md);
    }
    .bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-md);
      margin-bottom: var(--space-md);
    }
    .card {
      background: #ffffff;
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
    }
    .error {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      padding: var(--space-md);
    }
    .skeleton-rows {
      display: flex;
      flex-direction: column;
      gap: var(--space-sm);
      padding: var(--space-md);
    }
  `,
  template: `
    <h1 class="text-heading">Team</h1>
    <div class="bar">
      <input
        pInputText
        type="search"
        placeholder="Search by name"
        aria-label="Search team"
        [ngModel]="search()"
        (ngModelChange)="onSearch($event)"
      />
      <p-message severity="secondary" icon="pi pi-info-circle">Editing arrives in a later phase.</p-message>
    </div>

    @if (failed()) {
      <div class="card error" role="alert">
        <span>We could not load your team. Try again.</span>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="load()">Try again</button>
      </div>
    } @else if (loading() && items().length === 0) {
      <div class="card skeleton-rows" aria-hidden="true">
        @for (n of skeletonRows; track n) {
          <p-skeleton width="100%" height="40px" />
        }
      </div>
    } @else if (items().length === 0 && !search().trim()) {
      <app-empty-state
        icon="pi-users"
        title="No team members to show yet"
        body="Direct reports appear here once they are assigned."
      />
    } @else {
      <div class="card">
        <p-table
          [value]="items()"
          [lazy]="true"
          [lazyLoadOnInit]="false"
          (onLazyLoad)="onLazy($event)"
          [paginator]="true"
          [rows]="pageSize()"
          [first]="(page() - 1) * pageSize()"
          [totalRecords]="total()"
          [rowsPerPageOptions]="pageSizes"
          [showCurrentPageReport]="true"
          currentPageReportTemplate="Showing {first} to {last} of {totalRecords}"
          [loading]="loading()"
          aria-label="Team members"
          dataKey="employeeId"
        >
          <ng-template #header>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Designation</th>
              <th scope="col">Department</th>
              <th scope="col">Location</th>
            </tr>
          </ng-template>
          <ng-template #body let-row>
            <tr>
              <td>{{ row.name }}</td>
              <td>{{ row.designation }}</td>
              <td>{{ row.department }}</td>
              <td>{{ row.location }}</td>
            </tr>
          </ng-template>
          <ng-template #emptymessage>
            <tr>
              <td colspan="4">No team members match your search.</td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    }
  `,
})
export class TeamPage {
  private readonly api = inject(AdminApi);

  protected readonly pageSizes = PAGE_SIZES;
  protected readonly skeletonRows = [1, 2, 3, 4, 5, 6, 7, 8];
  protected readonly items = signal<TeamRow[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly search = signal('');
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private debounce: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.debounce) clearTimeout(this.debounce);
    });
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.team({ page: this.page(), pageSize: this.pageSize(), q: this.search().trim() || undefined }).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: () => {
        this.items.set([]);
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected onSearch(value: string): void {
    this.search.set(value ?? '');
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => {
      this.page.set(1);
      this.load();
    }, DEBOUNCE_MS);
  }

  protected onLazy(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.pageSize();
    this.pageSize.set(rows);
    this.page.set(Math.floor((event.first ?? 0) / rows) + 1);
    this.load();
  }
}
