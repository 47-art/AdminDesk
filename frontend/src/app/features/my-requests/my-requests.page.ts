import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Params, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { DatePicker } from 'primeng/datepicker';
import { Drawer } from 'primeng/drawer';
import { InputText } from 'primeng/inputtext';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Tooltip } from 'primeng/tooltip';

import { Observable, Subscription } from 'rxjs';

import { ApprovalStatus, MineQuery, Paged, RequestListItem, RequestStatus } from '../../core/api/models';
import { ModulesApi } from '../../core/api/modules.api';
import { RequestsApi } from '../../core/api/requests.api';
import { ROUTE_PATHS } from '../../core/constants/routes';
import {
  APPROVAL_STATUSES,
  REQUEST_STATUSES,
  REQUEST_STATUS_STYLES,
  STATUS_SORT_ORDER,
} from '../../core/constants/statuses';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { formatDate, parseDateOnly, relativeTime, toDateOnlyString } from '../../shared/formatters/dates';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';

type SortKey = NonNullable<MineQuery['sort']>;
type SortDir = NonNullable<MineQuery['dir']>;

const SORT_KEYS: readonly SortKey[] = ['requestNo', 'requestDate', 'status', 'updatedUtc'];
const PAGE_SIZES = [10, 20, 50];
const DEFAULT_PAGE_SIZE = 20;
const DEBOUNCE_MS = 300;

interface Option<T> {
  label: string;
  value: T | null;
}

interface ListState {
  q: string;
  status: RequestStatus[];
  approvalStatus: ApprovalStatus | null;
  module: string | null;
  from: string | null;
  to: string | null;
  sort: SortKey;
  dir: SortDir;
  page: number;
  pageSize: number;
}

function sourceOf(value: unknown): ListSource {
  return value === 'all' || value === 'team' ? value : 'mine';
}

function positiveInt(value: string | null, fallback: number): number {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : fallback;
}

/** Reads the URL query string into a list state; unknown values fall back to the defaults. */
function readState(params: ParamMap): ListState {
  const knownStatuses = Object.values(REQUEST_STATUSES) as RequestStatus[];
  const sort = params.get('sort') as SortKey | null;
  const approval = params.get('approvalStatus');
  const size = positiveInt(params.get('pageSize'), DEFAULT_PAGE_SIZE);
  return {
    q: params.get('q') ?? '',
    status: params.getAll('status').filter((s): s is RequestStatus => knownStatuses.includes(s as RequestStatus)),
    approvalStatus:
      approval && (Object.values(APPROVAL_STATUSES) as string[]).includes(approval) ? (approval as ApprovalStatus) : null,
    module: params.get('module') || null,
    from: params.get('from'),
    to: params.get('to'),
    sort: sort && SORT_KEYS.includes(sort) ? sort : 'requestDate',
    dir: params.get('dir') === 'asc' ? 'asc' : 'desc',
    page: positiveInt(params.get('page'), 1),
    pageSize: PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE,
  };
}

type ListSource = 'mine' | 'all' | 'team';

interface SourceConfig {
  heading: string;
  errorText: string;
  showNewRequest: boolean;
  showRequester: boolean;
  emptyIcon: string;
  emptyTitle: string;
  emptyBody: string;
  /** Empty hides the button. */
  emptyAction: string;
}

const SOURCES: Record<ListSource, SourceConfig> = {
  mine: {
    heading: 'My requests',
    errorText: 'We could not load your requests. Try again.',
    showNewRequest: true,
    showRequester: false,
    emptyIcon: 'pi-inbox',
    emptyTitle: 'You have not raised any requests yet',
    emptyBody: 'Start with a request type from the catalogue.',
    emptyAction: 'Start a request',
  },
  all: {
    heading: 'All requests',
    errorText: 'We could not load all requests. Try again.',
    showNewRequest: false,
    showRequester: true,
    emptyIcon: 'pi-inbox',
    emptyTitle: 'No requests yet',
    emptyBody: 'Requests raised by anyone in the organisation will appear here.',
    emptyAction: '',
  },
  team: {
    heading: 'Team requests',
    errorText: 'We could not load team requests. Try again.',
    showNewRequest: false,
    showRequester: true,
    emptyIcon: 'pi-users',
    emptyTitle: 'No team requests yet',
    emptyBody: 'Requests raised by people who report to you will appear here.',
    emptyAction: '',
  },
};

/**
 * A server-paged request list with search, filters and sorting kept in the URL.
 * Serves My requests, All requests and Team requests; the route data names the source.
 */
@Component({
  selector: 'app-my-requests-page',
  imports: [
    FormsModule,
    NgTemplateOutlet,
    RouterLink,
    ButtonDirective,
    DatePicker,
    Drawer,
    InputText,
    MultiSelect,
    Select,
    Skeleton,
    TableModule,
    Tooltip,
    EmptyStateComponent,
    StatusBadgeComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: var(--space-md);
    }
    .head h1 {
      margin: 0;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm);
      margin-bottom: var(--space-md);
    }
    .filters .grow {
      min-width: 180px;
    }
    .card {
      background: var(--p-surface-0, #ffffff);
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
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
    .sort-btn {
      all: unset;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-weight: 600;
    }
    .sort-btn:focus-visible {
      outline: 2px solid var(--p-primary-color);
      outline-offset: 2px;
    }
    tr.clickable {
      cursor: pointer;
    }
    tr.clickable:hover {
      background: var(--p-surface-100, #eef3f8);
    }
    .cards {
      display: flex;
      flex-direction: column;
      gap: var(--space-sm);
    }
    .req-card {
      background: var(--p-surface-0, #ffffff);
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
    .more {
      display: flex;
      justify-content: center;
      margin-top: var(--space-md);
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
    .drawer-body {
      display: flex;
      flex-direction: column;
      gap: var(--space-md);
    }
  `,
  template: `
    <div class="head">
      <h1 class="text-heading">{{ config.heading }}</h1>
      @if (config.showNewRequest) {
        <button pButton type="button" icon="pi pi-plus" label="New request" [routerLink]="['/', paths.NewRequest]"></button>
      }
    </div>

    @if (phone()) {
      <div class="filters">
        <input
          pInputText
          type="search"
          class="grow"
          placeholder="Search by ID or subject"
          aria-label="Search by ID or subject"
          [ngModel]="searchText()"
          (ngModelChange)="onSearch($event)"
        />
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
        <input
          pInputText
          type="search"
          class="grow"
          placeholder="Search by ID or subject"
          aria-label="Search by ID or subject"
          [ngModel]="searchText()"
          (ngModelChange)="onSearch($event)"
        />
        <ng-container *ngTemplateOutlet="filterFields" />
        <button pButton type="button" severity="secondary" [text]="true" (click)="clearFilters()">Clear filters</button>
      </div>
    }

    <ng-template #filterFields>
      <p-multiselect
        [options]="statusOptions"
        optionLabel="label"
        optionValue="value"
        [ngModel]="state().status"
        (ngModelChange)="setStatus($event)"
        placeholder="All statuses"
        ariaLabel="Status"
        [showToggleAll]="false"
        display="comma"
        class="grow"
      >
        <ng-template #item let-option>
          <app-status-badge [status]="option.value" />
        </ng-template>
      </p-multiselect>
      <p-select
        [options]="approvalOptions"
        optionLabel="label"
        optionValue="value"
        [ngModel]="state().approvalStatus"
        (ngModelChange)="patch({ approvalStatus: $event })"
        placeholder="All approval statuses"
        ariaLabel="Approval status"
        class="grow"
      />
      <p-select
        [options]="moduleOptions()"
        optionLabel="label"
        optionValue="value"
        [ngModel]="state().module"
        (ngModelChange)="patch({ module: $event })"
        placeholder="All modules"
        ariaLabel="Module"
        class="grow"
      />
      <p-datepicker
        selectionMode="range"
        dateFormat="dd/mm/yy"
        placeholder="Request date range"
        ariaLabel="Request date range"
        [showButtonBar]="true"
        [readonlyInput]="true"
        [ngModel]="dateRange()"
        (ngModelChange)="setDateRange($event)"
        class="grow"
      />
    </ng-template>

    @if (failed()) {
      <div class="card error" role="alert">
        <span>{{ config.errorText }}</span>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="reload()">Try again</button>
      </div>
    } @else if (loading() && items().length === 0) {
      <div class="card skeleton-rows" aria-hidden="true">
        @for (n of skeletonRows; track n) {
          <p-skeleton width="100%" height="40px" />
        }
      </div>
    } @else if (items().length === 0) {
      @if (activeFilterCount() > 0) {
        <app-empty-state
          icon="pi-filter"
          title="No requests match your filters"
          body="Try a wider date range or clear the filters."
          actionLabel="Clear filters"
          (action)="clearFilters()"
        />
      } @else {
        <app-empty-state
          [icon]="config.emptyIcon"
          [title]="config.emptyTitle"
          [body]="config.emptyBody"
          [actionLabel]="config.emptyAction"
          (action)="startRequest()"
        />
      }
    } @else if (phone()) {
      <div class="cards">
        @for (row of items(); track row.id) {
          <article class="req-card">
            <div class="first">
              <a class="link" [routerLink]="detailLink(row)">{{ row.requestNo }}</a>
              <app-status-badge [status]="row.currentStatus" />
            </div>
            <div>{{ row.subject }}</div>
            @if (config.showRequester) {
              <div class="secondary">{{ requesterLine(row) }}</div>
            }
            <div class="secondary">{{ row.moduleName }}</div>
            <div class="secondary">Requested {{ date(row.requestDate) }}</div>
          </article>
        }
      </div>
      @if (items().length < total()) {
        <div class="more">
          <button pButton type="button" severity="secondary" [outlined]="true" [disabled]="loading()" (click)="loadMore()">
            Load more
          </button>
        </div>
      }
    } @else {
      <div class="card">
        <p-table
          [value]="items()"
          [lazy]="true"
          [lazyLoadOnInit]="false"
          (onLazyLoad)="onLazy($event)"
          [paginator]="true"
          [rows]="state().pageSize"
          [first]="(state().page - 1) * state().pageSize"
          [totalRecords]="total()"
          [rowsPerPageOptions]="pageSizes"
          [showCurrentPageReport]="true"
          currentPageReportTemplate="Showing {first} to {last} of {totalRecords}"
          [loading]="loading()"
          [attr.aria-label]="config.heading"
          dataKey="id"
        >
          <ng-template #header>
            <tr>
              <th scope="col" [attr.aria-sort]="ariaSort('requestNo')">
                <button type="button" class="sort-btn" (click)="sortBy('requestNo')">
                  Request ID <i class="pi" [class]="sortIcon('requestNo')" aria-hidden="true"></i>
                </button>
              </th>
              @if (config.showRequester) {
                <th scope="col">Requester</th>
              }
              <th scope="col">Module</th>
              <th scope="col">Subject</th>
              <th scope="col" [attr.aria-sort]="ariaSort('requestDate')">
                <button type="button" class="sort-btn" (click)="sortBy('requestDate')">
                  Request date <i class="pi" [class]="sortIcon('requestDate')" aria-hidden="true"></i>
                </button>
              </th>
              <th scope="col">Required date</th>
              <th scope="col">Priority</th>
              <th scope="col" [attr.aria-sort]="ariaSort('status')">
                <button type="button" class="sort-btn" (click)="sortBy('status')">
                  Status <i class="pi" [class]="sortIcon('status')" aria-hidden="true"></i>
                </button>
              </th>
              <th scope="col">Current step</th>
              <th scope="col" [attr.aria-sort]="ariaSort('updatedUtc')">
                <button type="button" class="sort-btn" (click)="sortBy('updatedUtc')">
                  Last updated <i class="pi" [class]="sortIcon('updatedUtc')" aria-hidden="true"></i>
                </button>
              </th>
            </tr>
          </ng-template>
          <ng-template #body let-row>
            <tr class="clickable" (click)="open(row)">
              <td>
                <a class="link" [routerLink]="detailLink(row)" (click)="$event.stopPropagation()">{{ row.requestNo }}</a>
              </td>
              @if (config.showRequester) {
                <td>
                  {{ row.requesterName }}
                  @if (row.requesterDepartment) {
                    <div class="secondary">{{ row.requesterDepartment }}</div>
                  }
                </td>
              }
              <td>{{ row.moduleName }}</td>
              <td>{{ row.subject }}</td>
              <td>{{ date(row.requestDate) }}</td>
              <td>{{ row.requiredDate ? date(row.requiredDate) : '' }}</td>
              <td>{{ row.priority }}</td>
              <td><app-status-badge [status]="row.currentStatus" /></td>
              <td>
                @if (row.currentStepName) {
                  {{ row.currentStepName }}
                } @else {
                  <span aria-label="No current step">&mdash;</span>
                }
                @if (row.currentStatus === 'InProgress' && (row.responsibleName || row.responsibleRole)) {
                  <div class="secondary">Waiting on {{ row.responsibleName ?? row.responsibleRole }}</div>
                }
              </td>
              <td [pTooltip]="relative(row.updatedUtc).exact">{{ relative(row.updatedUtc).text }}</td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    }
  `,
})
export class MyRequestsPage implements OnInit {
  private readonly requestsApi = inject(RequestsApi);
  private readonly modulesApi = inject(ModulesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly paths = ROUTE_PATHS;
  protected readonly source: ListSource = sourceOf(this.route.snapshot.data['source']);
  protected readonly config = SOURCES[this.source];
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly skeletonRows = [1, 2, 3, 4, 5, 6, 7, 8];
  protected readonly statusOptions: Option<RequestStatus>[] = STATUS_SORT_ORDER.map((s) => ({
    label: REQUEST_STATUS_STYLES[s].label,
    value: s,
  }));
  protected readonly approvalOptions: Option<ApprovalStatus>[] = [
    { label: 'All approval statuses', value: null },
    ...(Object.values(APPROVAL_STATUSES) as ApprovalStatus[]).map((a) => ({ label: a, value: a })),
  ];

  protected readonly state = signal<ListState>(readState(this.route.snapshot.queryParamMap));
  protected readonly searchText = signal(this.state().q);
  protected readonly items = signal<RequestListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly phone = signal(false);
  protected readonly drawerOpen = signal(false);
  private readonly modules = signal<{ code: string; name: string }[]>([]);
  private debounce: ReturnType<typeof setTimeout> | null = null;
  private phonePage = 1;

  protected readonly moduleOptions = computed<Option<string>[]>(() => [
    { label: 'All modules', value: null },
    ...this.modules().map((m) => ({ label: m.name, value: m.code })),
  ]);

  protected readonly dateRange = computed<Date[] | null>(() => {
    const { from, to } = this.state();
    if (!from) return null;
    return [parseDateOnly(from), to ? parseDateOnly(to) : parseDateOnly(from)];
  });

  protected readonly activeFilterCount = computed(() => {
    const s = this.state();
    return (
      (s.q.trim() ? 1 : 0) +
      (s.status.length > 0 ? 1 : 0) +
      (s.approvalStatus ? 1 : 0) +
      (s.module ? 1 : 0) +
      (s.from ? 1 : 0)
    );
  });

  ngOnInit(): void {
    const query = window.matchMedia('(max-width: 767px)');
    this.phone.set(query.matches);
    const listener = (e: MediaQueryListEvent): void => {
      this.phone.set(e.matches);
      this.load();
    };
    query.addEventListener('change', listener);
    this.destroyRef.onDestroy(() => {
      query.removeEventListener('change', listener);
      if (this.debounce) clearTimeout(this.debounce);
    });

    this.modulesApi.list().subscribe({ next: (m) => this.modules.set(m), error: () => undefined });

    // The URL is the single source of truth: every change navigates, and every navigation reloads.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const next = readState(params);
      this.state.set(next);
      if (next.q !== this.searchText().trim()) this.searchText.set(next.q);
      this.load();
    });
  }

  protected date(value: string): string {
    return formatDate(value);
  }

  protected relative(value: string): { text: string; exact: string } {
    return relativeTime(value);
  }

  protected requesterLine(row: RequestListItem): string {
    return row.requesterDepartment ? `${row.requesterName}, ${row.requesterDepartment}` : row.requesterName;
  }

  protected detailLink(row: RequestListItem): unknown[] {
    return ['/', ROUTE_PATHS.RequestDetail, row.id];
  }

  protected open(row: RequestListItem): void {
    void this.router.navigate(this.detailLink(row));
  }

  protected startRequest(): void {
    void this.router.navigate(['/', ROUTE_PATHS.NewRequest]);
  }

  protected reload(): void {
    this.load();
  }

  protected ariaSort(key: SortKey): 'ascending' | 'descending' | 'none' {
    const s = this.state();
    if (s.sort !== key) return 'none';
    return s.dir === 'asc' ? 'ascending' : 'descending';
  }

  protected sortIcon(key: SortKey): string {
    const s = this.state();
    if (s.sort !== key) return 'pi-sort-alt';
    return s.dir === 'asc' ? 'pi-sort-amount-up-alt' : 'pi-sort-amount-down';
  }

  protected sortBy(key: SortKey): void {
    const s = this.state();
    const dir: SortDir = s.sort === key && s.dir === 'desc' ? 'asc' : 'desc';
    this.patch({ sort: key, dir });
  }

  protected onSearch(value: string): void {
    this.searchText.set(value ?? '');
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => this.patch({ q: (value ?? '').trim() }), DEBOUNCE_MS);
  }

  protected setStatus(value: RequestStatus[] | null): void {
    this.patch({ status: value ?? [] });
  }

  protected setDateRange(value: Date[] | null): void {
    if (!value || !value[0]) {
      this.patch({ from: null, to: null });
      return;
    }
    if (!value[1]) return; // wait for the end of the range
    this.patch({ from: toDateOnlyString(value[0]), to: toDateOnlyString(value[1]) });
  }

  protected onLazy(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.state().pageSize;
    const first = event.first ?? 0;
    const page = Math.floor(first / rows) + 1;
    const s = this.state();
    if (rows !== s.pageSize || page !== s.page) {
      this.patch({ pageSize: rows, page }, false);
    }
  }

  protected clearFilters(): void {
    this.searchText.set('');
    this.navigate({ ...this.state(), q: '', status: [], approvalStatus: null, module: null, from: null, to: null, page: 1 });
    this.drawerOpen.set(false);
  }

  protected loadMore(): void {
    const next = this.phonePage + 1;
    this.inFlight?.unsubscribe();
    this.loading.set(true);
    this.inFlight = this.fetch({ ...this.toQuery(this.state()), page: next, pageSize: DEFAULT_PAGE_SIZE }).subscribe({
      next: (result) => {
        this.phonePage = next;
        this.items.update((current) => [...current, ...result.items]);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** Merges a change into the state and writes it to the URL; filter, sort and size changes go back to page 1. */
  protected patch(change: Partial<ListState>, resetPage = true): void {
    this.navigate({ ...this.state(), ...change, page: resetPage && !('page' in change) ? 1 : (change.page ?? this.state().page) });
  }

  private navigate(next: ListState): void {
    const queryParams: Params = {
      q: next.q || null,
      status: next.status.length > 0 ? next.status : null,
      approvalStatus: next.approvalStatus,
      module: next.module,
      from: next.from,
      to: next.to,
      sort: next.sort === 'requestDate' ? null : next.sort,
      dir: next.dir === 'desc' ? null : next.dir,
      page: next.page > 1 ? next.page : null,
      pageSize: next.pageSize !== DEFAULT_PAGE_SIZE ? next.pageSize : null,
    };
    void this.router.navigate([], { relativeTo: this.route, queryParams, replaceUrl: false });
  }

  private toQuery(s: ListState): MineQuery {
    return {
      q: s.q || undefined,
      status: s.status.length > 0 ? s.status : undefined,
      approvalStatus: s.approvalStatus ?? undefined,
      module: s.module ?? undefined,
      from: s.from ?? undefined,
      to: s.to ?? undefined,
      sort: s.sort,
      dir: s.dir,
      page: s.page,
      pageSize: s.pageSize,
    };
  }

  private fetch(query: MineQuery): Observable<Paged<RequestListItem>> {
    switch (this.source) {
      case 'all':
        return this.requestsApi.all(query);
      case 'team':
        return this.requestsApi.team(query);
      default:
        return this.requestsApi.mine(query);
    }
  }

  /** The request in flight; a newer one replaces it so an older answer cannot overwrite newer results. */
  private inFlight?: Subscription;

  private load(): void {
    this.inFlight?.unsubscribe();
    const s = this.state();
    this.loading.set(true);
    this.failed.set(false);
    const phone = this.phone();
    this.phonePage = 1;
    const query = phone ? { ...this.toQuery(s), page: 1, pageSize: DEFAULT_PAGE_SIZE } : this.toQuery(s);
    this.inFlight = this.fetch(query).subscribe({
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
}
