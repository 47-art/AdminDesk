import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ButtonDirective } from 'primeng/button';
import { DatePicker } from 'primeng/datepicker';
import { Dialog } from 'primeng/dialog';
import { Drawer } from 'primeng/drawer';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { Tab, TabList, Tabs } from 'primeng/tabs';
import { Tag } from 'primeng/tag';
import { ToggleSwitch } from 'primeng/toggleswitch';

import { ApiError, userMessage } from '../../core/api/api-error';
import {
  AssetRecord,
  IdCardRecord,
  MASTER_KINDS,
  MasterHistoryEvent,
  MasterKind,
  MastersApi,
  SimRecord,
} from '../../core/api/masters.api';
import { Paged } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { ROLE_GROUPS, Role } from '../../core/constants/roles';
import { ROUTE_PATHS } from '../../core/constants/routes';
import {
  ASSET_STATUS_OPTIONS,
  ID_CARD_STATUS_OPTIONS,
  MASTER_STATUS_STYLES,
  MasterStatus,
  SIM_STATUS_OPTIONS,
  StatusStyle,
} from '../../core/constants/statuses';
import { NotificationService } from '../../core/notifications/notification.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { FieldErrorComponent } from '../../shared/field-error/field-error.component';
import { formatDate, formatDateTime, parseDateOnly, toDateOnlyString } from '../../shared/formatters/dates';
import { formatInr } from '../../shared/formatters/money';
import { LookupFieldComponent } from '../../shared/lookup-field/lookup-field.component';
import { PageSkeletonComponent } from '../../shared/page-skeleton/page-skeleton.component';
import { FieldGroup, applyServerErrors, errorMessage } from '../new-request/form-builder';

const DEBOUNCE_MS = 300;
const PAGE_SIZES = [10, 20, 50];
const UNKNOWN_STATUS: StatusStyle = { label: '', background: '#E8ECF0', text: '#455363', icon: 'pi-circle' };

type FormFieldType = 'text' | 'money' | 'date' | 'employee';

interface FormFieldSpec {
  key: string;
  label: string;
  type: FormFieldType;
  maxLength?: number;
  /** Only on the add form. */
  addOnly?: boolean;
}

interface MasterSpec {
  kind: MasterKind;
  tab: string;
  singular: string;
  viewers: readonly Role[];
  editors: readonly Role[];
  statuses: MasterStatus[];
  searchHint: string;
  empty: { icon: string; title: string; body: string };
  fields: FormFieldSpec[];
}

const MASTER_SPECS: readonly MasterSpec[] = [
  {
    kind: MASTER_KINDS.Sims,
    tab: 'SIMs',
    singular: 'SIM',
    viewers: ROLE_GROUPS.SimMasterViewers,
    editors: ROLE_GROUPS.SimMasterEditors,
    statuses: SIM_STATUS_OPTIONS,
    searchHint: 'Search by SIM number, mobile number or holder',
    empty: { icon: 'pi-mobile', title: 'No SIMs to show', body: 'SIM cards appear here once they are added.' },
    fields: [
      { key: 'simNumber', label: 'SIM number', type: 'text', maxLength: 30 },
      { key: 'mobileNumber', label: 'Mobile number', type: 'text', maxLength: 20 },
      { key: 'telecomOperator', label: 'Operator', type: 'text', maxLength: 60 },
      { key: 'plan', label: 'Plan', type: 'text', maxLength: 80 },
      { key: 'monthlyCost', label: 'Monthly cost', type: 'money' },
    ],
  },
  {
    kind: MASTER_KINDS.Assets,
    tab: 'Assets',
    singular: 'asset',
    viewers: ROLE_GROUPS.AssetMasterViewers,
    editors: ROLE_GROUPS.AssetMasterEditors,
    statuses: ASSET_STATUS_OPTIONS,
    searchHint: 'Search by tag, model, serial number or holder',
    empty: { icon: 'pi-desktop', title: 'No assets to show', body: 'Laptops and other assets appear here once they are added.' },
    fields: [
      { key: 'assetTag', label: 'Asset tag', type: 'text', maxLength: 40 },
      { key: 'assetType', label: 'Asset type', type: 'text', maxLength: 40 },
      { key: 'makeModel', label: 'Make and model', type: 'text', maxLength: 120 },
      { key: 'serialNumber', label: 'Serial number', type: 'text', maxLength: 60 },
    ],
  },
  {
    kind: MASTER_KINDS.IdCards,
    tab: 'ID cards',
    singular: 'ID card',
    viewers: ROLE_GROUPS.IdCardMasterViewers,
    editors: ROLE_GROUPS.IdCardMasterEditors,
    statuses: ID_CARD_STATUS_OPTIONS,
    searchHint: 'Search by card number or employee',
    empty: { icon: 'pi-id-card', title: 'No ID cards to show', body: 'ID cards appear here once they are issued.' },
    fields: [
      { key: 'cardNumber', label: 'Card number', type: 'text', maxLength: 40 },
      { key: 'employeeId', label: 'Employee', type: 'employee', addOnly: true },
      { key: 'issuedDate', label: 'Issued date', type: 'date' },
    ],
  },
];

type AnyRecord = SimRecord | AssetRecord | IdCardRecord;

/** SIM, asset and ID card masters: status and current holder, history, and maintenance for each owner. */
@Component({
  selector: 'app-masters-page',
  imports: [
    FormsModule,
    ReactiveFormsModule,
    RouterLink,
    ButtonDirective,
    DatePicker,
    Dialog,
    Drawer,
    InputNumber,
    InputText,
    Select,
    Skeleton,
    TableModule,
    Tabs,
    TabList,
    Tab,
    Tag,
    ToggleSwitch,
    EmptyStateComponent,
    FieldErrorComponent,
    LookupFieldComponent,
    PageSkeletonComponent,
  ],
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
      margin: var(--space-md) 0;
    }
    .spacer {
      flex: 1;
    }
    .card {
      background: var(--p-surface-0, #ffffff);
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
    .secondary {
      display: block;
      font-size: 12px;
      color: var(--p-text-muted-color);
    }
    .muted {
      color: var(--p-text-muted-color);
    }
    .actions {
      display: flex;
      gap: var(--space-xs);
      justify-content: flex-end;
      white-space: nowrap;
    }
    .num {
      text-align: right;
    }
    .retired-switch {
      display: inline-flex;
      align-items: center;
      gap: var(--space-sm);
      font-size: 14px;
      font-weight: 400;
    }
    :host ::ng-deep tr.retired-row > td {
      color: var(--p-text-muted-color);
    }
    .status-cell {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-xs);
    }
    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
    }
    .link {
      color: var(--p-primary-color);
      text-decoration: none;
      font-weight: 600;
    }
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-md);
    }
    .form-field {
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
    }
    label {
      font-size: 12px;
      font-weight: 600;
    }
    .note {
      margin: 0 0 var(--space-md);
    }
    .notes {
      overflow-wrap: anywhere;
      white-space: pre-line;
    }
    :host ::ng-deep .full-width {
      width: 100%;
    }
  `,
  template: `
    <h1 class="text-heading">Masters</h1>

    <p-tabs [value]="kind()" (valueChange)="selectTab($event)">
      <p-tablist>
        @for (spec of visibleSpecs(); track spec.kind) {
          <p-tab [value]="spec.kind">{{ spec.tab }}</p-tab>
        }
      </p-tablist>
    </p-tabs>

    <div class="bar">
      <input
        pInputText
        type="search"
        [placeholder]="spec().searchHint"
        [attr.aria-label]="'Search ' + spec().tab"
        [ngModel]="search()"
        (ngModelChange)="onSearch($event)"
      />
      <p-select
        [options]="spec().statuses"
        [ngModel]="status()"
        (ngModelChange)="onStatus($event)"
        placeholder="Any status"
        [showClear]="true"
        [attr.aria-label]="'Filter ' + spec().tab + ' by status'"
      />
      @if (canEdit()) {
        <label class="retired-switch">
          <p-toggleswitch [ngModel]="showRetired()" (ngModelChange)="onShowRetired($event)" ariaLabel="Show retired" />
          Show retired
        </label>
      }
      <span class="spacer"></span>
      @if (canEdit()) {
        <button pButton type="button" icon="pi pi-plus" [label]="'Add ' + spec().singular" (click)="openAdd()"></button>
      }
    </div>

    @if (failed()) {
      <div class="card error" role="alert">
        <span>We could not load the list. Try again.</span>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="load()">Try again</button>
      </div>
    } @else if (loading() && items().length === 0) {
      <div class="card skeleton-rows" aria-hidden="true">
        @for (n of skeletonRows; track n) {
          <p-skeleton width="100%" height="40px" />
        }
      </div>
    } @else if (items().length === 0 && !search().trim() && !status() && !showRetired()) {
      <app-empty-state [icon]="spec().empty.icon" [title]="spec().empty.title" [body]="spec().empty.body" />
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
          [scrollable]="true"
          [attr.aria-label]="spec().tab"
          dataKey="id"
        >
          <ng-template #header>
            <tr>
              @switch (kind()) {
                @case (kinds.Sims) {
                  <th scope="col">SIM number</th>
                  <th scope="col">Mobile number</th>
                  <th scope="col">Operator</th>
                  <th scope="col">Plan</th>
                  <th scope="col">Activated</th>
                  <th scope="col">Status</th>
                  <th scope="col" class="num">Monthly cost</th>
                  <th scope="col">Holder</th>
                }
                @case (kinds.Assets) {
                  <th scope="col">Tag</th>
                  <th scope="col">Type</th>
                  <th scope="col">Make and model</th>
                  <th scope="col">Serial number</th>
                  <th scope="col">Status</th>
                  <th scope="col">Condition</th>
                  <th scope="col">Holder</th>
                }
                @case (kinds.IdCards) {
                  <th scope="col">Card number</th>
                  <th scope="col">Employee</th>
                  <th scope="col">Status</th>
                  <th scope="col">Issued</th>
                }
              }
              <th scope="col"><span class="visually-hidden">Actions</span></th>
            </tr>
          </ng-template>
          <ng-template #body let-row>
            <tr [class.retired-row]="row.retired">
              @switch (kind()) {
                @case (kinds.Sims) {
                  <td>{{ row.simNumber }}</td>
                  <td>{{ row.mobileNumber }}</td>
                  <td>{{ row.telecomOperator }}</td>
                  <td>{{ row.plan }}</td>
                  <td>{{ dateOf(row.activationDate) }}</td>
                  <td>
                    <div class="status-cell">
                      <p-tag [value]="statusStyle(row.status).label || row.status" [icon]="'pi ' + statusStyle(row.status).icon" [style]="tagStyle(row.status)" />
                      @if (row.retired) {
                        <p-tag value="Retired" icon="pi pi-ban" [style]="tagStyle('Deactivated')" />
                      }
                    </div>
                  </td>
                  <td class="num">{{ money(row.monthlyCost) }}</td>
                  <td>
                    @if (row.holderName) {
                      {{ row.holderName }}<span class="secondary">{{ row.holderCode }}</span>
                    } @else {
                      <span class="muted">Not held</span>
                    }
                  </td>
                }
                @case (kinds.Assets) {
                  <td>{{ row.assetTag }}</td>
                  <td>{{ row.assetType }}</td>
                  <td>{{ row.makeModel }}</td>
                  <td>{{ row.serialNumber }}</td>
                  <td>
                    <div class="status-cell">
                      <p-tag [value]="statusStyle(row.status).label || row.status" [icon]="'pi ' + statusStyle(row.status).icon" [style]="tagStyle(row.status)" />
                      @if (row.retired) {
                        <p-tag value="Retired" icon="pi pi-ban" [style]="tagStyle('Deactivated')" />
                      }
                    </div>
                  </td>
                  <td>{{ row.condition ?? '' }}</td>
                  <td>
                    @if (row.holderName) {
                      {{ row.holderName }}<span class="secondary">{{ row.holderCode }}</span>
                    } @else {
                      <span class="muted">Not held</span>
                    }
                  </td>
                }
                @case (kinds.IdCards) {
                  <td>{{ row.cardNumber }}</td>
                  <td>
                    {{ row.employeeName }}<span class="secondary">{{ row.employeeCode }}</span>
                  </td>
                  <td>
                    <div class="status-cell">
                      <p-tag [value]="statusStyle(row.status).label || row.status" [icon]="'pi ' + statusStyle(row.status).icon" [style]="tagStyle(row.status)" />
                      @if (row.retired) {
                        <p-tag value="Retired" icon="pi pi-ban" [style]="tagStyle('Deactivated')" />
                      }
                    </div>
                  </td>
                  <td>{{ dateOf(row.issuedDate) }}</td>
                }
              }
              <td>
                <div class="actions">
                  <button pButton type="button" severity="secondary" [text]="true" size="small" icon="pi pi-history" label="History" (click)="openHistory(row)"></button>
                  @if (canEdit() && !row.retired) {
                    <button pButton type="button" severity="secondary" [text]="true" size="small" icon="pi pi-pencil" label="Edit" (click)="openEdit(row)"></button>
                    <button
                      pButton
                      type="button"
                      severity="danger"
                      [text]="true"
                      size="small"
                      icon="pi pi-trash"
                      label="Retire"
                      [disabled]="isHeld(row)"
                      [attr.title]="isHeld(row) ? 'Return or replace it before retiring' : null"
                      (click)="askRetire(row)"
                    ></button>
                  }
                </div>
              </td>
            </tr>
          </ng-template>
          <ng-template #emptymessage>
            <tr>
              <td [attr.colspan]="9">Nothing matches your search or filter.</td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    }

    <p-dialog
      [visible]="formOpen()"
      (visibleChange)="onFormVisible($event)"
      [modal]="true"
      [draggable]="false"
      [closable]="!busy()"
      [closeOnEscape]="!busy()"
      [header]="formTitle()"
      [style]="{ width: '480px', maxWidth: 'calc(100vw - 16px)' }"
    >
      <p class="note muted">Holder and status are set by requests and cannot be edited here.</p>
      <div class="form">
        @for (f of formFields(); track f.key) {
          <div class="form-field">
            <label [for]="fieldId(f)">{{ f.label }}</label>
            @switch (f.type) {
              @case ('money') {
                <p-inputnumber
                  styleClass="full-width"
                  inputStyleClass="full-width"
                  mode="currency"
                  currency="INR"
                  locale="en-IN"
                  [min]="0"
                  [minFractionDigits]="2"
                  [maxFractionDigits]="2"
                  [inputId]="fieldId(f)"
                  [formControl]="ctl(f.key)"
                />
              }
              @case ('date') {
                <p-datepicker
                  styleClass="full-width"
                  inputStyleClass="full-width"
                  dateFormat="dd/mm/yy"
                  [showIcon]="true"
                  appendTo="body"
                  [inputId]="fieldId(f)"
                  [formControl]="ctl(f.key)"
                />
              }
              @case ('employee') {
                <app-lookup-field kind="employee" placeholder="Type to search" [inputId]="fieldId(f)" [formControl]="ctl(f.key)" />
              }
              @default {
                <input pInputText type="text" class="full-width" [id]="fieldId(f)" [attr.maxlength]="f.maxLength" [formControl]="ctl(f.key)" />
              }
            }
            <app-field-error [id]="fieldId(f) + '-error'" [message]="message(f.key)" />
          </div>
        }
      </div>
      <ng-template #footer>
        <button pButton type="button" severity="secondary" [text]="true" [disabled]="busy()" (click)="closeForm()">Cancel</button>
        <button pButton type="button" [loading]="busy()" [disabled]="busy()" (click)="save()">
          {{ editing() ? 'Save changes' : 'Add ' + spec().singular }}
        </button>
      </ng-template>
    </p-dialog>

    <p-dialog
      [visible]="retireTarget() !== null"
      (visibleChange)="onRetireVisible($event)"
      [modal]="true"
      [draggable]="false"
      [closable]="!busy()"
      [closeOnEscape]="!busy()"
      [header]="'Retire this ' + spec().singular + '?'"
      [style]="{ width: '440px', maxWidth: 'calc(100vw - 16px)' }"
    >
      <p>
        {{ retireLabel() }} will disappear from the lists, but its history stays and can still be read. This is
        not undone from this screen.
      </p>
      <ng-template #footer>
        <button pButton type="button" severity="secondary" [text]="true" [disabled]="busy()" (click)="retireTarget.set(null)">Keep it</button>
        <button pButton type="button" severity="danger" [loading]="busy()" [disabled]="busy()" (click)="confirmRetire()">Retire</button>
      </ng-template>
    </p-dialog>

    <p-drawer
      [visible]="historyOpen()"
      (visibleChange)="onHistoryVisible($event)"
      position="right"
      [header]="historyTitle()"
      [modal]="true"
      [dismissible]="true"
      [closeOnEscape]="true"
      [style]="{ width: 'min(960px, 100vw)' }"
    >
      @if (historyLoading()) {
        <app-page-skeleton preset="rows" />
      } @else if (historyFailed()) {
        <div class="error">
          <span>We could not load the history. Try again.</span>
          <button pButton type="button" severity="secondary" [outlined]="true" size="small" (click)="loadHistory()">Try again</button>
        </div>
      } @else if (history().length === 0) {
        <p class="muted">No history recorded yet</p>
      } @else {
        <p-table [value]="history()" size="small" [scrollable]="true" aria-label="History">
          <ng-template #header>
            <tr>
              <th scope="col">Date</th>
              <th scope="col">Event</th>
              <th scope="col">Employee</th>
              <th scope="col">Request</th>
              <th scope="col">Condition</th>
              <th scope="col" class="num">Cost</th>
              <th scope="col">Notes</th>
            </tr>
          </ng-template>
          <ng-template #body let-e>
            <tr>
              <td>{{ time(e.eventUtc) }}</td>
              <td>{{ e.eventType }}</td>
              <td>
                @if (e.employeeName) {
                  {{ e.employeeName }}<span class="secondary">{{ e.employeeCode }}</span>
                }
              </td>
              <td>
                @if (e.requestId) {
                  <a class="link" [routerLink]="['/', paths.RequestDetail, e.requestId]">{{ e.requestNo }}</a>
                }
              </td>
              <td>{{ e.condition ?? '' }}</td>
              <td class="num">{{ e.cost === null ? '' : money(e.cost) }}</td>
              <td class="notes">{{ e.notes ?? '' }}</td>
            </tr>
          </ng-template>
        </p-table>
      }
    </p-drawer>
  `,
})
export class MastersPage {
  private readonly api = inject(MastersApi);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  protected readonly kinds = MASTER_KINDS;
  protected readonly paths = ROUTE_PATHS;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly skeletonRows = [1, 2, 3, 4, 5, 6];

  protected readonly visibleSpecs = computed(() => MASTER_SPECS.filter((s) => this.auth.hasAnyRole(s.viewers)));
  protected readonly kind = signal<MasterKind>(MASTER_KINDS.Sims);
  protected readonly spec = computed(() => MASTER_SPECS.find((s) => s.kind === this.kind()) ?? MASTER_SPECS[0]);
  protected readonly canEdit = computed(() => this.auth.hasAnyRole(this.spec().editors));

  protected readonly items = signal<AnyRecord[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly search = signal('');
  protected readonly status = signal<string | null>(null);
  protected readonly showRetired = signal(false);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private debounce: ReturnType<typeof setTimeout> | null = null;
  private loadToken = 0;

  // Add and edit
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<AnyRecord | null>(null);
  protected readonly busy = signal(false);
  protected readonly formGroup = signal<FieldGroup>(new FormGroup({}));
  private readonly formTick = signal(0);
  protected readonly formFields = computed(() => this.spec().fields.filter((f) => !f.addOnly || !this.editing()));
  protected readonly formTitle = computed(
    () => `${this.editing() ? 'Edit' : 'Add'} ${this.spec().singular}`,
  );

  // Retire
  protected readonly retireTarget = signal<AnyRecord | null>(null);
  protected readonly retireLabel = computed(() => {
    const row = this.retireTarget();
    return row ? this.labelOf(row) : 'It';
  });

  // History
  protected readonly historyOpen = signal(false);
  protected readonly historyTarget = signal<AnyRecord | null>(null);
  protected readonly history = signal<MasterHistoryEvent[]>([]);
  protected readonly historyLoading = signal(false);
  protected readonly historyFailed = signal(false);
  protected readonly historyTitle = computed(() => {
    const row = this.historyTarget();
    return row ? `History of ${this.labelOf(row)}` : 'History';
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.debounce) clearTimeout(this.debounce);
    });
    const first = this.visibleSpecs()[0];
    if (first) this.kind.set(first.kind);
    this.load();
  }

  // ---------------------------------------------------------------- list

  protected selectTab(value: string | number | undefined): void {
    const next = MASTER_SPECS.find((s) => s.kind === value);
    if (!next || next.kind === this.kind()) return;
    this.kind.set(next.kind);
    this.search.set('');
    this.status.set(null);
    this.showRetired.set(false);
    this.page.set(1);
    this.items.set([]);
    this.total.set(0);
    this.load();
  }

  protected load(): void {
    const token = ++this.loadToken;
    const kind = this.kind();
    this.loading.set(true);
    this.failed.set(false);
    const query = {
      page: this.page(),
      pageSize: this.pageSize(),
      search: this.search().trim() || undefined,
      status: this.status() || undefined,
      includeRetired: this.canEdit() && this.showRetired() ? true : undefined,
    };
    const call =
      kind === MASTER_KINDS.Sims
        ? this.api.sims(query)
        : kind === MASTER_KINDS.Assets
          ? this.api.assets(query)
          : this.api.idCards(query);
    (call as Observable<Paged<AnyRecord>>).subscribe({
      next: (result) => {
        if (token !== this.loadToken) return;
        this.items.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: () => {
        if (token !== this.loadToken) return;
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

  protected onStatus(value: string | null): void {
    this.status.set(value);
    this.page.set(1);
    this.load();
  }

  protected onShowRetired(value: boolean): void {
    this.showRetired.set(!!value);
    this.page.set(1);
    this.load();
  }

  protected onLazy(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.pageSize();
    this.pageSize.set(rows);
    this.page.set(Math.floor((event.first ?? 0) / rows) + 1);
    this.load();
  }

  // ------------------------------------------------------------- display

  protected statusStyle(status: string): StatusStyle {
    return MASTER_STATUS_STYLES[status as MasterStatus] ?? { ...UNKNOWN_STATUS, label: status };
  }

  protected tagStyle(status: string): Record<string, string> {
    const s = this.statusStyle(status);
    return { background: s.background, color: s.text, 'font-size': '12px', 'font-weight': '600', padding: '4px 8px', 'border-radius': '6px' };
  }

  protected dateOf(value: string | null): string {
    return value ? formatDate(parseDateOnly(value)) : '';
  }

  protected money(value: number): string {
    return formatInr(value);
  }

  protected time(value: string): string {
    return formatDateTime(value);
  }

  protected isHeld(row: AnyRecord): boolean {
    if ('cardNumber' in row) return row.status === 'Active';
    return row.holderEmployeeId !== null;
  }

  private labelOf(row: AnyRecord): string {
    if ('simNumber' in row) return `SIM ${row.simNumber}`;
    if ('assetTag' in row) return `Asset ${row.assetTag}`;
    return `Card ${row.cardNumber}`;
  }

  // ------------------------------------------------------------- history

  protected openHistory(row: AnyRecord): void {
    this.historyTarget.set(row);
    this.historyOpen.set(true);
    this.loadHistory();
  }

  protected loadHistory(): void {
    const row = this.historyTarget();
    if (!row) return;
    this.historyLoading.set(true);
    this.historyFailed.set(false);
    this.api.history(this.kind(), row.id).subscribe({
      next: (events) => {
        if (this.historyTarget() !== row) return;
        this.history.set(events);
        this.historyLoading.set(false);
      },
      error: () => {
        if (this.historyTarget() !== row) return;
        this.historyFailed.set(true);
        this.historyLoading.set(false);
      },
    });
  }

  protected onHistoryVisible(open: boolean): void {
    this.historyOpen.set(open);
    if (!open) {
      this.historyTarget.set(null);
      this.history.set([]);
    }
  }

  // ----------------------------------------------------- add, edit, retire

  protected fieldId(f: FormFieldSpec): string {
    return `master-field-${f.key}`;
  }

  protected ctl(key: string): FormControl<unknown> {
    return this.formGroup().controls[key];
  }

  protected message(key: string): string | null {
    this.formTick();
    return errorMessage(this.formGroup().controls[key]);
  }

  protected openAdd(): void {
    this.editing.set(null);
    this.buildForm(null);
    this.formOpen.set(true);
  }

  protected openEdit(row: AnyRecord): void {
    this.editing.set(row);
    this.buildForm(row);
    this.formOpen.set(true);
  }

  private buildForm(row: AnyRecord | null): void {
    const record = (row ?? {}) as unknown as Record<string, unknown>;
    const controls: Record<string, FormControl<unknown>> = {};
    for (const f of this.spec().fields) {
      if (f.addOnly && row) continue;
      let initial: unknown;
      if (f.type === 'date') {
        const raw = record[f.key];
        initial = typeof raw === 'string' ? parseDateOnly(raw) : null;
      } else if (f.type === 'money') {
        initial = typeof record[f.key] === 'number' ? record[f.key] : null;
      } else if (f.type === 'employee') {
        initial = null;
      } else {
        initial = typeof record[f.key] === 'string' ? record[f.key] : '';
      }
      const validators = [Validators.required];
      if (f.maxLength) validators.push(Validators.maxLength(f.maxLength));
      controls[f.key] = new FormControl<unknown>(initial, validators);
    }
    const group: FieldGroup = new FormGroup(controls);
    group.events.subscribe(() => this.formTick.update((n) => n + 1));
    this.formGroup.set(group);
  }

  protected onFormVisible(open: boolean): void {
    if (!open) this.closeForm();
  }

  protected closeForm(): void {
    if (this.busy()) return;
    this.formOpen.set(false);
  }

  protected save(): void {
    if (this.busy()) return;
    const group = this.formGroup();
    group.markAllAsTouched();
    this.formTick.update((n) => n + 1);
    if (group.invalid) return;

    const body: Record<string, unknown> = {};
    for (const f of this.formFields()) {
      const value = group.controls[f.key].value;
      if (f.type === 'date') body[f.key] = value instanceof Date ? toDateOnlyString(value) : null;
      else if (f.type === 'text') body[f.key] = typeof value === 'string' ? value.trim() : value;
      else body[f.key] = value;
    }

    const row = this.editing();
    const kind = this.kind();
    this.busy.set(true);
    const call = row ? this.api.edit<AnyRecord>(kind, row.id, body) : this.api.add<AnyRecord>(kind, body);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        this.formOpen.set(false);
        this.notifications.success(row ? `${this.spec().singular} updated` : `${this.spec().singular} added`);
        this.load();
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.handleError(err);
      },
    });
  }

  protected askRetire(row: AnyRecord): void {
    this.retireTarget.set(row);
  }

  protected onRetireVisible(open: boolean): void {
    if (!open && !this.busy()) this.retireTarget.set(null);
  }

  protected confirmRetire(): void {
    const row = this.retireTarget();
    if (!row || this.busy()) return;
    this.busy.set(true);
    this.api.retire(this.kind(), row.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.retireTarget.set(null);
        this.notifications.success(`${this.labelOf(row)} retired`);
        this.load();
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.retireTarget.set(null);
        this.handleError(err);
      },
    });
  }

  private handleError(err: unknown): void {
    if (err instanceof ApiError && err.status === 400 && err.fieldErrors.length > 0 && this.formOpen()) {
      const result = applyServerErrors(this.formGroup(), null, err.fieldErrors);
      this.formTick.update((n) => n + 1);
      for (const e of result.unmatched) this.notifications.error(e.message);
      return;
    }
    if (err instanceof ApiError && (err.status === 0 || err.status >= 500 || err.status === 401 || err.status === 403)) {
      return;
    }
    this.notifications.error(userMessage(err));
  }
}
