import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Drawer } from 'primeng/drawer';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Tooltip } from 'primeng/tooltip';

import { userMessage } from '../../core/api/api-error';
import { FieldDto, ModuleSummary, Priority, RequestDetail, RequestListItem } from '../../core/api/models';
import { ModulesApi } from '../../core/api/modules.api';
import { RequestsApi } from '../../core/api/requests.api';
import { PRIORITIES } from '../../core/constants/field-types';
import { ROLE_GROUPS } from '../../core/constants/roles';
import { ROUTE_PATHS } from '../../core/constants/routes';
import { REQUEST_STATUS_STYLES, STEP_STATE_STYLES } from '../../core/constants/statuses';
import { AuthService } from '../../core/auth/auth.service';
import { BadgeCountsService } from '../../core/state/badge-counts.service';
import { NotificationService } from '../../core/notifications/notification.service';
import {
  ActionDialogComponent,
  CONFLICT_MESSAGE,
  DialogAction,
  RequestActionRunner,
} from '../../shared/action-dialog/action-dialog.component';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PageSkeletonComponent } from '../../shared/page-skeleton/page-skeleton.component';

const AMBER_AFTER_DAYS = 3;
const RED_AFTER_DAYS = 7;
const DEBOUNCE_MS = 300;
const PAGE_SIZES = [10, 20, 50];

interface Option<T> {
  label: string;
  value: T | null;
}

/** "Waiting for me": requests whose current step needs the signed-in user, oldest first. */
@Component({
  selector: 'app-inbox-page',
  imports: [
    FormsModule,
    NgTemplateOutlet,
    RouterLink,
    ButtonDirective,
    Drawer,
    InputText,
    Select,
    TableModule,
    Tooltip,
    ActionDialogComponent,
    EmptyStateComponent,
    PageSkeletonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .head {
      display: flex;
      align-items: baseline;
      gap: var(--space-md);
      margin-bottom: var(--space-md);
    }
    .head h1 {
      margin: 0;
    }
    .secondary {
      color: var(--p-text-muted-color);
      font-size: 13px;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm);
      margin-bottom: var(--space-md);
    }
    .filters .grow {
      min-width: 200px;
    }
    .card {
      background: #ffffff;
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
    }
    .link {
      color: var(--p-primary-color);
      text-decoration: none;
      font-weight: 600;
    }
    .row-actions {
      display: flex;
      gap: var(--space-xs);
    }
    .cards {
      display: flex;
      flex-direction: column;
      gap: var(--space-sm);
    }
    .req-card {
      background: #ffffff;
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      padding: var(--space-md);
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
    }
    .req-card .first {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .req-card .buttons {
      display: flex;
      gap: var(--space-sm);
      margin-top: var(--space-sm);
    }
    .req-card .buttons button {
      flex: 1 1 0;
      height: 44px;
    }
    .error {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      padding: var(--space-md);
    }
    .drawer-body {
      display: flex;
      flex-direction: column;
      gap: var(--space-md);
    }
    .paging {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: var(--space-sm) var(--space-md);
    }
  `,
  template: `
    <div class="head">
      <h1 class="text-heading">Waiting for me</h1>
      <span class="secondary">{{ total() }} {{ total() === 1 ? 'request' : 'requests' }}</span>
    </div>

    @if (phone()) {
      <div class="filters">
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="drawerOpen.set(true)">
          Filters{{ activeFilterCount() > 0 ? ' (' + activeFilterCount() + ')' : '' }}
        </button>
      </div>
      <p-drawer [(visible)]="drawerOpen" position="bottom" header="Filters" [style]="{ height: 'auto' }">
        <div class="drawer-body">
          <ng-container *ngTemplateOutlet="filterFields" />
          <button pButton type="button" severity="secondary" [text]="true" (click)="clearFilters()">Clear filters</button>
        </div>
      </p-drawer>
    } @else {
      <div class="filters" role="search">
        <ng-container *ngTemplateOutlet="filterFields" />
        <button pButton type="button" severity="secondary" [text]="true" (click)="clearFilters()">Clear filters</button>
      </div>
    }

    <ng-template #filterFields>
      <p-select
        [options]="moduleOptions()"
        optionLabel="label"
        optionValue="value"
        [ngModel]="module()"
        (ngModelChange)="setModule($event)"
        placeholder="All modules"
        [showClear]="false"
        ariaLabel="Module"
        class="grow"
      />
      <input
        pInputText
        type="search"
        class="grow"
        placeholder="Requester"
        aria-label="Requester"
        [ngModel]="requester()"
        (ngModelChange)="setRequester($event)"
      />
      <p-select
        [options]="priorityOptions"
        optionLabel="label"
        optionValue="value"
        [ngModel]="priority()"
        (ngModelChange)="setPriority($event)"
        placeholder="All priorities"
        ariaLabel="Priority"
        class="grow"
      />
    </ng-template>

    @if (loading() && items().length === 0) {
      <app-page-skeleton preset="rows" />
    } @else if (failed()) {
      <div class="card error" role="alert">
        <span>We could not load your inbox. Try again.</span>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="load()">Try again</button>
      </div>
    } @else if (items().length === 0) {
      @if (activeFilterCount() > 0) {
        <app-empty-state
          icon="pi-filter"
          title="No requests match your filters"
          actionLabel="Clear filters"
          (action)="clearFilters()"
        />
      } @else {
        <app-empty-state
          icon="pi-check-circle"
          [iconColour]="caughtUpColour"
          title="You are all caught up"
          body="Nothing is waiting for your decision. New items appear here when someone sends a request your way."
        />
      }
    } @else if (phone()) {
      <div class="cards">
        @for (row of items(); track row.id) {
          <article class="req-card">
            <div class="first">
              <a class="link" [routerLink]="detailLink(row)">{{ row.requestNo }}</a>
              <span [style.color]="ageColour(row)">{{ row.ageDays }}d</span>
            </div>
            <div>{{ row.moduleName }}: {{ row.subject }}</div>
            <div class="secondary">{{ row.requesterName }}</div>
            <div class="secondary">{{ row.currentStepName }}</div>
            <div class="buttons">
              <button
                pButton
                type="button"
                [disabled]="isBusy(row)"
                [attr.aria-label]="label(row) + ' ' + row.requestNo"
                (click)="primary(row)"
              >
                {{ label(row) }}
              </button>
              @if (canReject(row)) {
                <button
                  pButton
                  type="button"
                  severity="danger"
                  [outlined]="true"
                  [disabled]="isBusy(row)"
                  [attr.aria-label]="'Reject ' + row.requestNo"
                  (click)="reject(row)"
                >
                  Reject
                </button>
              }
            </div>
          </article>
        }
      </div>
      <div class="paging">
        <button pButton type="button" severity="secondary" [outlined]="true" [disabled]="page() <= 1" (click)="goTo(page() - 1)">Previous</button>
        <span class="secondary">Page {{ page() }}</span>
        <button pButton type="button" severity="secondary" [outlined]="true" [disabled]="page() * pageSize() >= total()" (click)="goTo(page() + 1)">Next</button>
      </div>
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
          [loading]="loading()"
          aria-label="Requests waiting for you"
          dataKey="id"
        >
          <ng-template #header>
            <tr>
              <th scope="col">Request ID</th>
              <th scope="col">Module</th>
              <th scope="col">Subject</th>
              <th scope="col">Requester</th>
              <th scope="col">Step</th>
              <th scope="col">Age</th>
              <th scope="col" pTooltip="Service levels arrive in a later phase">SLA</th>
              <th scope="col">Actions</th>
            </tr>
          </ng-template>
          <ng-template #body let-row>
            <tr>
              <td><a class="link" [routerLink]="detailLink(row)">{{ row.requestNo }}</a></td>
              <td>{{ row.moduleName }}</td>
              <td>{{ row.subject }}</td>
              <td>
                {{ row.requesterName }}
                @if (row.requesterDepartment) {
                  <div class="secondary">{{ row.requesterDepartment }}</div>
                }
              </td>
              <td>{{ row.currentStepName }}</td>
              <td [style.color]="ageColour(row)">
                @if (row.ageDays > amberAfter) {
                  <i class="pi pi-clock" aria-hidden="true"></i>
                }
                {{ row.ageDays }} {{ row.ageDays === 1 ? 'day' : 'days' }}
              </td>
              <td class="secondary">Not set</td>
              <td>
                <div class="row-actions">
                  <button
                    pButton
                    type="button"
                    size="small"
                    [disabled]="isBusy(row)"
                    [attr.aria-label]="label(row) + ' ' + row.requestNo"
                    (click)="primary(row)"
                  >
                    {{ label(row) }}
                  </button>
                  @if (canReject(row)) {
                    <button
                      pButton
                      type="button"
                      size="small"
                      severity="danger"
                      [outlined]="true"
                      [disabled]="isBusy(row)"
                      [attr.aria-label]="'Reject ' + row.requestNo"
                      (click)="reject(row)"
                    >
                      Reject
                    </button>
                  }
                </div>
              </td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    }

    @if (dialogRow(); as row) {
      <app-action-dialog
        [(visible)]="dialogVisible"
        [requestId]="row.detail.id"
        [requestNo]="row.detail.requestNo"
        [action]="row.action"
        [rowVersion]="row.detail.rowVersion"
        [captureFields]="row.captureFields"
        [primaryActionLabel]="row.label"
        (completed)="afterChange()"
        (conflict)="afterConflict()"
      />
    }
  `,
})
export class InboxPage implements OnInit {
  private readonly requestsApi = inject(RequestsApi);
  private readonly modulesApi = inject(ModulesApi);
  private readonly runner = inject(RequestActionRunner);
  private readonly auth = inject(AuthService);
  private readonly badges = inject(BadgeCountsService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly amberAfter = AMBER_AFTER_DAYS;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly caughtUpColour = REQUEST_STATUS_STYLES.Closed.text;
  protected readonly priorityOptions: Option<Priority>[] = [
    { label: 'All priorities', value: null },
    ...Object.values(PRIORITIES).map((p) => ({ label: p, value: p as Priority })),
  ];

  protected readonly items = signal<RequestListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly phone = signal(false);
  protected readonly drawerOpen = signal(false);

  protected readonly module = signal<string | null>(null);
  protected readonly requester = signal('');
  protected readonly priority = signal<Priority | null>(null);
  private readonly modules = signal<ModuleSummary[]>([]);
  private readonly busyIds = signal<ReadonlySet<number>>(new Set());
  private debounce: ReturnType<typeof setTimeout> | null = null;

  protected readonly dialogVisible = signal(false);
  protected readonly dialogRow = signal<{
    detail: RequestDetail;
    action: DialogAction;
    captureFields: FieldDto[];
    label: string | null;
  } | null>(null);

  protected readonly isOverride = computed(() => this.auth.hasAnyRole(ROLE_GROUPS.RequestOverride));
  protected readonly moduleOptions = computed<Option<string>[]>(() => [
    { label: 'All modules', value: null },
    ...this.modules().map((m) => ({ label: m.name, value: m.code })),
  ]);

  protected readonly activeFilterCount = computed(
    () => (this.module() ? 1 : 0) + (this.requester().trim() ? 1 : 0) + (this.priority() ? 1 : 0),
  );

  ngOnInit(): void {
    const query = window.matchMedia('(max-width: 767px)');
    this.phone.set(query.matches);
    const listener = (e: MediaQueryListEvent): void => this.phone.set(e.matches);
    query.addEventListener('change', listener);
    this.destroyRef.onDestroy(() => {
      query.removeEventListener('change', listener);
      if (this.debounce) clearTimeout(this.debounce);
    });

    this.modulesApi.list().subscribe({ next: (m) => this.modules.set(m), error: () => undefined });
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.requestsApi
      .inbox({
        page: this.page(),
        pageSize: this.pageSize(),
        module: this.module() ?? undefined,
        requester: this.requester().trim() || undefined,
        priority: this.priority() ?? undefined,
      })
      .subscribe({
        next: (result) => {
          if (result.items.length === 0 && result.total > 0 && this.page() > 1) {
            this.page.set(1);
            this.load();
            return;
          }
          this.items.set(result.items);
          this.total.set(result.total);
          this.loading.set(false);
          this.badges.refreshInbox();
        },
        error: () => {
          this.items.set([]);
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected onLazy(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.pageSize();
    const first = event.first ?? 0;
    this.pageSize.set(rows);
    this.page.set(Math.floor(first / rows) + 1);
    this.load();
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected setModule(value: string | null): void {
    this.module.set(value);
    this.page.set(1);
    this.load();
  }

  protected setPriority(value: Priority | null): void {
    this.priority.set(value);
    this.page.set(1);
    this.load();
  }

  protected setRequester(value: string): void {
    this.requester.set(value ?? '');
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => {
      this.page.set(1);
      this.load();
    }, DEBOUNCE_MS);
  }

  protected clearFilters(): void {
    this.module.set(null);
    this.requester.set('');
    this.priority.set(null);
    this.page.set(1);
    this.load();
  }

  protected detailLink(row: RequestListItem): unknown[] {
    return ['/', ROUTE_PATHS.RequestDetail, row.id];
  }

  /** Reject shows on approval rows for everyone, and on every row for the override role. The server decides. */
  protected canReject(row: RequestListItem): boolean {
    return row.currentStepType === 'Approval' || this.isOverride();
  }

  protected label(row: RequestListItem): string {
    return row.primaryActionLabel ?? (row.currentStepType === 'Approval' ? 'Approve' : 'Complete');
  }

  protected ageColour(row: RequestListItem): string | null {
    if (row.ageDays > RED_AFTER_DAYS) return REQUEST_STATUS_STYLES.Rejected.text;
    if (row.ageDays > AMBER_AFTER_DAYS) return STEP_STATE_STYLES.Pending.text;
    return null;
  }

  protected isBusy(row: RequestListItem): boolean {
    return this.busyIds().has(row.id);
  }

  /** Approve at an approval row, Complete at a task row. */
  protected primary(row: RequestListItem): void {
    const action = row.currentStepType === 'Approval' ? 'Approve' : 'Complete';
    this.withLatest(row, (detail) => {
      if (action === 'Complete' && row.captureFields.length > 0) {
        this.openDialog(detail, 'Complete', row);
        return;
      }
      this.markBusy(row.id, true);
      this.runner.run(detail.id, detail.requestNo, action, detail.rowVersion).subscribe((outcome) => {
        this.markBusy(row.id, false);
        if (outcome.kind !== 'failed') this.afterChange();
      });
    });
  }

  protected reject(row: RequestListItem): void {
    this.withLatest(row, (detail) => this.openDialog(detail, 'Reject', row));
  }

  protected afterChange(): void {
    this.load();
  }

  protected afterConflict(): void {
    this.load();
  }

  /** The list carries no row version, so the current one is read just before acting. */
  private withLatest(row: RequestListItem, then: (detail: RequestDetail) => void): void {
    this.markBusy(row.id, true);
    this.requestsApi.get(row.id).subscribe({
      next: (detail) => {
        this.markBusy(row.id, false);
        if (detail.currentStatus !== 'InProgress' || detail.allowedActions.length === 0) {
          this.notifications.error(CONFLICT_MESSAGE);
          this.load();
          return;
        }
        then(detail);
      },
      error: (err: unknown) => {
        this.markBusy(row.id, false);
        this.notifications.error(userMessage(err));
        this.load();
      },
    });
  }

  private openDialog(detail: RequestDetail, action: DialogAction, row: RequestListItem): void {
    this.dialogRow.set({
      detail,
      action,
      captureFields: action === 'Complete' ? row.captureFields : [],
      label: row.primaryActionLabel,
    });
    this.dialogVisible.set(true);
  }

  private markBusy(id: number, busy: boolean): void {
    this.busyIds.update((set) => {
      const next = new Set(set);
      if (busy) next.add(id);
      else next.delete(id);
      return next;
    });
  }
}
