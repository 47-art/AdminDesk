import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';
import { SelectButton } from 'primeng/selectbutton';
import { Skeleton } from 'primeng/skeleton';

import { AdminConfigApi, ConditionFieldChoice, ConditionInput, ConfigLimit, ConfigStep, ModuleConfig } from '../../core/api/admin-config.api';
import { ApiError, userMessage } from '../../core/api/api-error';
import { FIELD_TYPES } from '../../core/constants/field-types';
import { NotificationService } from '../../core/notifications/notification.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { FieldErrorComponent } from '../../shared/field-error/field-error.component';
import { LIST_OPERATORS, NO_OPERAND_OPERATORS, OPERATOR_WORDS, conditionSentence, limitLabel } from './condition-text';

const STATE_CONFLICT_CODE = 'STATE_CONFLICT';
const NEW_REQUESTS_ONLY = 'Changes apply to new requests only; requests already in progress keep their rules.';
/** Operators that make sense when comparing against a limit. */
const LIMIT_OPERATORS: readonly string[] = ['eq', 'neq', 'gt', 'gte', 'lt', 'lte'];

type Mode = 'value' | 'limit';

interface ChoiceGroup {
  label: string;
  items: { value: string; label: string }[];
}

/** Lets the people who own money policy change approval limits and step conditions. */
@Component({
  selector: 'app-limits-conditions-page',
  imports: [FormsModule, ButtonDirective, Dialog, InputNumber, InputText, Message, MultiSelect, Select, SelectButton, Skeleton, EmptyStateComponent, FieldErrorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    h1 {
      margin: 0 0 var(--space-md);
    }
    h2 {
      margin: 0 0 var(--space-sm);
      font-size: 18px;
    }
    .bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-md);
      margin-bottom: var(--space-md);
    }
    .version {
      color: var(--p-text-muted-color);
    }
    .card {
      background: var(--p-surface-0, #ffffff);
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      padding: var(--space-md);
      margin-bottom: var(--space-md);
    }
    .note {
      margin: 0 0 var(--space-md);
      color: var(--p-text-muted-color);
      font-size: 14px;
    }
    .grid {
      width: 100%;
      border-collapse: collapse;
    }
    .grid th,
    .grid td {
      text-align: left;
      padding: var(--space-sm);
      border-bottom: 1px solid var(--p-content-border-color);
      vertical-align: middle;
    }
    .grid th {
      font-weight: 600;
      font-size: 13px;
      color: var(--p-text-muted-color);
    }
    .actor {
      color: var(--p-text-muted-color);
      font-size: 13px;
    }
    .inline-unit {
      margin-left: var(--space-xs);
      color: var(--p-text-muted-color);
    }
    .row-actions {
      white-space: nowrap;
    }
    .error {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      padding: var(--space-md);
      background: var(--p-surface-0, #ffffff);
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
    }
    .skeleton-rows {
      display: flex;
      flex-direction: column;
      gap: var(--space-sm);
    }
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-md);
    }
    .form label {
      display: block;
      margin-bottom: var(--space-xs);
      font-weight: 600;
      font-size: 14px;
    }
    .full {
      width: 100%;
    }
    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
      white-space: nowrap;
    }
    .readonly-rule {
      padding: var(--space-sm);
      background: var(--p-surface-50, #f4f8fc);
      border-radius: 6px;
    }
  `,
  template: `
    <h1 class="text-heading">Limits and conditions</h1>

    @if (failed()) {
      <div class="error" role="alert">
        <span>We could not load the module settings. Try again.</span>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="load()">Try again</button>
      </div>
    } @else if (loading() && modules().length === 0) {
      <div class="skeleton-rows" aria-hidden="true">
        <p-skeleton width="320px" height="40px" />
        <p-skeleton width="100%" height="160px" />
        <p-skeleton width="100%" height="200px" />
      </div>
    } @else if (modules().length === 0) {
      <app-empty-state icon="pi-sliders-h" title="No modules to configure" body="Modules appear here once their definitions are loaded." />
    } @else {
      <div class="bar">
        <p-select
          [options]="moduleOptions()"
          optionLabel="label"
          optionValue="value"
          [ngModel]="selectedCode()"
          (ngModelChange)="selectModule($event)"
          ariaLabel="Module"
          [style]="{ minWidth: '280px' }"
        />
        @if (config(); as c) {
          <span class="version">Version {{ c.version }}</span>
        }
      </div>

      @if (conflict()) {
        <p-message severity="warn" icon="pi pi-exclamation-triangle" styleClass="full">
          Someone else changed this module while you were editing.
          <button pButton type="button" severity="secondary" [text]="true" (click)="reload()">Reload</button>
        </p-message>
      }

      @if (config(); as c) {
        <section class="card" aria-labelledby="limits-heading">
          <h2 id="limits-heading" class="text-heading">Approval limits</h2>
          <p class="note">{{ newRequestsOnly }}</p>
          @if (c.limits.length === 0) {
            <app-empty-state icon="pi-sliders-h" title="No limits for this module" body="This module does not declare any approval limits." />
          } @else {
            <table class="grid" aria-label="Approval limits">
              <thead>
                <tr>
                  <th scope="col">Step</th>
                  <th scope="col">Limit</th>
                  <th scope="col">Amount</th>
                  <th scope="col"><span class="sr-only">Save</span></th>
                </tr>
              </thead>
              <tbody>
                @for (l of c.limits; track rowKey(l)) {
                  <tr>
                    <td>{{ stepName(c, l.stepKey) }}</td>
                    <td>{{ label(l.limitKey) }}</td>
                    <td>
                      <p-inputnumber
                        mode="currency"
                        currency="INR"
                        locale="en-IN"
                        [useGrouping]="true"
                        [minFractionDigits]="2"
                        [maxFractionDigits]="2"
                        [min]="0"
                        [inputId]="'limit-' + rowKey(l)"
                        [ngModel]="draft()[rowKey(l)]"
                        (ngModelChange)="setDraft(l, $event)"
                        [attr.aria-label]="label(l.limitKey) + ' for ' + stepName(c, l.stepKey)"
                      />
                      @if (l.unit) {
                        <span class="inline-unit">{{ l.unit }}</span>
                      }
                      <app-field-error [id]="'limit-err-' + rowKey(l)" [message]="limitErrors()[rowKey(l)]" />
                    </td>
                    <td class="row-actions">
                      <button
                        pButton
                        type="button"
                        size="small"
                        [disabled]="!isDirty(l) || savingKey() !== null"
                        [loading]="savingKey() === rowKey(l)"
                        (click)="saveLimit(l)"
                      >
                        Save
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </section>

        <section class="card" aria-labelledby="cond-heading">
          <h2 id="cond-heading" class="text-heading">Step conditions</h2>
          <p class="note">{{ newRequestsOnly }}</p>
          <table class="grid" aria-label="Step conditions">
            <thead>
              <tr>
                <th scope="col">Step</th>
                <th scope="col">Condition</th>
                <th scope="col"><span class="sr-only">Edit</span></th>
              </tr>
            </thead>
            <tbody>
              @for (s of c.steps; track s.key) {
                <tr>
                  <td>
                    {{ s.name }}
                    <div class="actor">{{ s.actorLabel }}</div>
                  </td>
                  <td>{{ sentence(s) }}</td>
                  <td class="row-actions">
                    <button pButton type="button" size="small" severity="secondary" [outlined]="true" (click)="openEditor(s)" [attr.aria-label]="'Edit condition for ' + s.name">
                      Edit
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      }
    }

    <p-dialog
      [visible]="dialogOpen()"
      (visibleChange)="onDialogVisible($event)"
      [modal]="true"
      [closable]="!busy()"
      [closeOnEscape]="!busy()"
      [draggable]="false"
      [header]="dialogTitle()"
      [style]="{ width: '520px', maxWidth: 'calc(100vw - 16px)' }"
    >
      @if (editing(); as step) {
        <div class="form">
          <p class="note">{{ newRequestsOnly }}</p>

          @if (!step.conditionEditable) {
            <div>
              <div class="readonly-rule">Currently: {{ sentence(step) }}</div>
              <p class="note">This step uses a group of rules that cannot be edited here. Saving a single rule replaces it.</p>
            </div>
          }

          <div>
            <label for="cond-field">Field</label>
            <p-select
              inputId="cond-field"
              [options]="fieldGroups()"
              [group]="true"
              optionLabel="label"
              optionValue="value"
              optionGroupLabel="label"
              optionGroupChildren="items"
              [ngModel]="fieldKey()"
              (ngModelChange)="onFieldChange($event)"
              placeholder="Choose a field"
              styleClass="full"
              appendTo="body"
              [invalid]="!!errors()['condition.field']"
            />
            <app-field-error id="cond-field-err" [message]="errors()['condition.field']" />
          </div>

          <div>
            <label for="cond-op">Operator</label>
            <p-select
              inputId="cond-op"
              [options]="operatorOptions()"
              optionLabel="label"
              optionValue="value"
              [ngModel]="op()"
              (ngModelChange)="onOpChange($event)"
              placeholder="Choose an operator"
              styleClass="full"
              appendTo="body"
              [disabled]="!choice()"
              [invalid]="!!errors()['condition.op']"
            />
            <app-field-error id="cond-op-err" [message]="errors()['condition.op']" />
          </div>

          @if (choice() && needsOperand()) {
            @if (canCompareToLimit()) {
              <div>
                <label id="cond-mode-label">Compare to</label>
                <p-selectbutton
                  [options]="modeOptions"
                  optionLabel="label"
                  optionValue="value"
                  [allowEmpty]="false"
                  [ngModel]="mode()"
                  (ngModelChange)="onModeChange($event)"
                  ariaLabelledBy="cond-mode-label"
                />
              </div>
            }

            @if (mode() === 'limit') {
              <div>
                <label for="cond-limit">Limit</label>
                <p-select
                  inputId="cond-limit"
                  [options]="limitOptions()"
                  optionLabel="label"
                  optionValue="value"
                  [ngModel]="limitKey()"
                  (ngModelChange)="limitKey.set($event)"
                  placeholder="Choose a limit"
                  styleClass="full"
                  appendTo="body"
                  [invalid]="!!errors()['condition.limit']"
                />
                <app-field-error id="cond-limit-err" [message]="errors()['condition.limit']" />
              </div>
            } @else {
              <div>
                <label for="cond-value">Value</label>
                @switch (valueKind()) {
                  @case ('money') {
                    <p-inputnumber
                      inputId="cond-value"
                      mode="currency"
                      currency="INR"
                      locale="en-IN"
                      [useGrouping]="true"
                      [minFractionDigits]="2"
                      [maxFractionDigits]="2"
                      styleClass="full"
                      inputStyleClass="full"
                      [ngModel]="numberValue()"
                      (ngModelChange)="numberValue.set($event)"
                    />
                  }
                  @case ('number') {
                    <p-inputnumber
                      inputId="cond-value"
                      locale="en-IN"
                      [useGrouping]="true"
                      [minFractionDigits]="0"
                      [maxFractionDigits]="2"
                      styleClass="full"
                      inputStyleClass="full"
                      [ngModel]="numberValue()"
                      (ngModelChange)="numberValue.set($event)"
                    />
                  }
                  @case ('yesno') {
                    <p-select
                      inputId="cond-value"
                      [options]="yesNoOptions"
                      optionLabel="label"
                      optionValue="value"
                      [ngModel]="boolValue()"
                      (ngModelChange)="boolValue.set($event)"
                      placeholder="Choose"
                      styleClass="full"
                      appendTo="body"
                    />
                  }
                  @case ('select') {
                    <p-select
                      inputId="cond-value"
                      [options]="choiceOptions()"
                      optionLabel="label"
                      optionValue="value"
                      [ngModel]="textValue()"
                      (ngModelChange)="textValue.set($event)"
                      placeholder="Choose"
                      styleClass="full"
                      appendTo="body"
                    />
                  }
                  @case ('multiselect') {
                    <p-multiselect
                      inputId="cond-value"
                      [options]="choiceOptions()"
                      optionLabel="label"
                      optionValue="value"
                      [ngModel]="listValue()"
                      (ngModelChange)="listValue.set($event ?? [])"
                      placeholder="Choose one or more"
                      styleClass="full"
                      appendTo="body"
                    />
                  }
                  @case ('list') {
                    <input pInputText id="cond-value" class="full" [ngModel]="textValue()" (ngModelChange)="textValue.set($event)" placeholder="Separate values with commas" />
                  }
                  @case ('date') {
                    <input pInputText id="cond-value" class="full" type="date" [ngModel]="textValue()" (ngModelChange)="textValue.set($event)" />
                  }
                  @case ('datetime') {
                    <input pInputText id="cond-value" class="full" type="datetime-local" [ngModel]="textValue()" (ngModelChange)="textValue.set($event)" />
                  }
                  @default {
                    <input pInputText id="cond-value" class="full" [ngModel]="textValue()" (ngModelChange)="textValue.set($event)" />
                  }
                }
                <app-field-error id="cond-value-err" [message]="errors()['condition.value']" />
              </div>
            }
          }

          @if (generalError()) {
            <p-message severity="error" icon="pi pi-exclamation-circle">{{ generalError() }}</p-message>
          }
        </div>
      }
      <ng-template #footer>
        <button pButton type="button" severity="secondary" [text]="true" [disabled]="busy()" (click)="closeDialog()">Cancel</button>
        @if (editing()?.condition) {
          <button pButton type="button" severity="secondary" [outlined]="true" [disabled]="busy()" (click)="saveCondition(true)">Always required</button>
        }
        <button pButton type="button" [loading]="busy()" [disabled]="busy()" (click)="saveCondition(false)">Save condition</button>
      </ng-template>
    </p-dialog>
  `,
})
export class LimitsConditionsPage {
  private readonly api = inject(AdminConfigApi);
  private readonly notifications = inject(NotificationService);

  protected readonly newRequestsOnly = NEW_REQUESTS_ONLY;
  protected readonly modeOptions = [
    { label: 'A value', value: 'value' },
    { label: 'A limit', value: 'limit' },
  ];
  protected readonly yesNoOptions = [
    { label: 'Yes', value: true },
    { label: 'No', value: false },
  ];

  protected readonly modules = signal<ModuleConfig[]>([]);
  protected readonly selectedCode = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly conflict = signal(false);

  protected readonly config = computed(() => this.modules().find((m) => m.code === this.selectedCode()) ?? null);
  protected readonly moduleOptions = computed(() =>
    this.modules().map((m) => ({ label: `${m.name} (version ${m.version})`, value: m.code })),
  );

  // Limits
  protected readonly draft = signal<Record<string, number | null>>({});
  protected readonly savingKey = signal<string | null>(null);
  protected readonly limitErrors = signal<Record<string, string>>({});

  // Condition editor
  protected readonly editing = signal<ConfigStep | null>(null);
  protected readonly dialogOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly errors = signal<Record<string, string>>({});
  protected readonly generalError = signal<string | null>(null);
  protected readonly fieldKey = signal<string | null>(null);
  protected readonly op = signal<string | null>(null);
  protected readonly mode = signal<Mode>('value');
  protected readonly limitKey = signal<string | null>(null);
  protected readonly numberValue = signal<number | null>(null);
  protected readonly textValue = signal('');
  protected readonly boolValue = signal<boolean | null>(null);
  protected readonly listValue = signal<string[]>([]);

  protected readonly dialogTitle = computed(() => {
    const step = this.editing();
    return step ? `Condition for ${step.name}` : 'Condition';
  });

  protected readonly choice = computed<ConditionFieldChoice | null>(() => {
    const step = this.editing();
    return step?.conditionFields.find((c) => c.key === this.fieldKey()) ?? null;
  });

  protected readonly fieldGroups = computed<ChoiceGroup[]>(() => {
    const step = this.editing();
    if (!step) return [];
    const own = step.conditionFields.filter((c) => !c.key.includes('.'));
    const earlier = step.conditionFields.filter((c) => c.key.includes('.'));
    const groups: ChoiceGroup[] = [{ label: 'Form fields', items: own.map((c) => ({ value: c.key, label: c.label })) }];
    if (earlier.length > 0) {
      groups.push({ label: 'Captured by earlier steps', items: earlier.map((c) => ({ value: c.key, label: c.label })) });
    }
    return groups;
  });

  protected readonly canCompareToLimit = computed(() => {
    const step = this.editing();
    const c = this.choice();
    const op = this.op();
    return !!step && !!c && c.allowsLimit && step.limitKeys.length > 0 && !!op && LIMIT_OPERATORS.includes(op);
  });

  protected readonly operatorOptions = computed(() => {
    const c = this.choice();
    if (!c) return [];
    return c.operators.map((o) => ({ value: o, label: OPERATOR_WORDS[o] ?? o }));
  });

  protected readonly limitOptions = computed(() =>
    (this.editing()?.limitKeys ?? []).map((k) => ({ value: k, label: limitLabel(k) })),
  );

  protected readonly choiceOptions = computed(() =>
    (this.choice()?.options ?? []).map((o) => ({ value: o.value, label: o.label })),
  );

  protected readonly needsOperand = computed(() => {
    const op = this.op();
    return !!op && !NO_OPERAND_OPERATORS.includes(op);
  });

  protected readonly valueKind = computed(() => {
    const c = this.choice();
    const isList = LIST_OPERATORS.includes(this.op() ?? '');
    if (!c) return 'text';
    const hasOptions = c.options.length > 0;
    if (isList) return hasOptions ? 'multiselect' : 'list';
    switch (c.type) {
      case FIELD_TYPES.Money:
        return 'money';
      case FIELD_TYPES.Number:
      case FIELD_TYPES.Lookup:
        return 'number';
      case FIELD_TYPES.YesNo:
        return 'yesno';
      case FIELD_TYPES.Date:
        return 'date';
      case FIELD_TYPES.DateTime:
        return 'datetime';
      default:
        return hasOptions ? 'select' : 'text';
    }
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.list().subscribe({
      next: (list) => {
        this.modules.set(list);
        const keep = list.some((m) => m.code === this.selectedCode());
        this.selectedCode.set(keep ? this.selectedCode() : (list[0]?.code ?? null));
        this.resetDraft();
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected reload(): void {
    this.conflict.set(false);
    this.load();
  }

  protected selectModule(code: string): void {
    this.selectedCode.set(code);
    this.conflict.set(false);
    this.limitErrors.set({});
    this.resetDraft();
  }

  protected label(key: string): string {
    return limitLabel(key);
  }

  protected sentence(step: ConfigStep): string {
    return conditionSentence(step);
  }

  protected stepName(c: ModuleConfig, stepKey: string): string {
    return c.steps.find((s) => s.key === stepKey)?.name ?? stepKey;
  }

  // ------------------------------------------------------------------ limits

  protected rowKey(l: ConfigLimit): string {
    return `${l.stepKey}__${l.limitKey}`;
  }

  protected setDraft(l: ConfigLimit, value: number | null): void {
    this.draft.update((d) => ({ ...d, [this.rowKey(l)]: value }));
  }

  protected isDirty(l: ConfigLimit): boolean {
    const value = this.draft()[this.rowKey(l)];
    return value !== null && value !== undefined && toMinor(value) !== l.valueMinor;
  }

  protected saveLimit(l: ConfigLimit): void {
    const c = this.config();
    const value = this.draft()[this.rowKey(l)];
    if (!c || value === null || value === undefined) return;
    const key = this.rowKey(l);
    this.savingKey.set(key);
    this.limitErrors.update((e) => ({ ...e, [key]: '' }));
    this.api.updateLimit(c.code, l.stepKey, l.limitKey, toMinor(value)).subscribe({
      next: (updated) => {
        this.replaceModule(updated);
        this.savingKey.set(null);
        this.notifications.success(`${limitLabel(l.limitKey)} saved. ${updated.name} is now version ${updated.version}.`);
      },
      error: (err: unknown) => {
        this.savingKey.set(null);
        if (this.isConflict(err)) return;
        const message = err instanceof ApiError ? (err.fieldMessage('valueMinor') ?? userMessage(err)) : userMessage(err);
        this.limitErrors.update((e) => ({ ...e, [key]: message }));
      },
    });
  }

  // ------------------------------------------------------------------ conditions

  protected openEditor(step: ConfigStep): void {
    this.editing.set(step);
    this.errors.set({});
    this.generalError.set(null);
    this.fieldKey.set(null);
    this.op.set(null);
    this.mode.set('value');
    this.limitKey.set(null);
    this.numberValue.set(null);
    this.textValue.set('');
    this.boolValue.set(null);
    this.listValue.set([]);

    const rule = step.condition;
    if (rule && step.conditionEditable && rule.field) {
      this.fieldKey.set(rule.field);
      this.op.set(rule.op);
      if (rule.limit) {
        this.mode.set('limit');
        this.limitKey.set(rule.limit);
      } else {
        this.loadValue(rule.value);
      }
    }
    this.dialogOpen.set(true);
  }

  private loadValue(value: unknown): void {
    if (Array.isArray(value)) {
      if (this.valueKind() === 'multiselect') {
        this.listValue.set(value.map((v) => String(v)));
      } else {
        this.textValue.set(value.map((v) => String(v)).join(', '));
      }
      return;
    }
    if (typeof value === 'number') this.numberValue.set(value);
    else if (typeof value === 'boolean') this.boolValue.set(value);
    else if (typeof value === 'string') this.textValue.set(value);
  }

  protected onFieldChange(key: string): void {
    this.fieldKey.set(key);
    this.errors.set({});
    this.op.set(null);
    this.mode.set('value');
    this.limitKey.set(null);
    this.clearValues();
  }

  protected onOpChange(op: string): void {
    const wasList = LIST_OPERATORS.includes(this.op() ?? '');
    this.op.set(op);
    if (wasList !== LIST_OPERATORS.includes(op)) this.clearValues();
    if (!LIMIT_OPERATORS.includes(op)) {
      this.mode.set('value');
      this.limitKey.set(null);
    }
  }

  protected onModeChange(mode: Mode): void {
    this.mode.set(mode);
    this.errors.update((e) => ({ ...e, 'condition.value': '', 'condition.limit': '' }));
  }

  private clearValues(): void {
    this.numberValue.set(null);
    this.textValue.set('');
    this.boolValue.set(null);
    this.listValue.set([]);
  }

  protected onDialogVisible(visible: boolean): void {
    if (!visible) this.closeDialog();
  }

  protected closeDialog(): void {
    if (this.busy()) return;
    this.dialogOpen.set(false);
  }

  /** Builds the rule from the dropdowns; the server validates it again. */
  private buildCondition(): ConditionInput | null {
    const field = this.fieldKey();
    const op = this.op();
    if (!field || !op) {
      return { field: field ?? '', op: op ?? '' };
    }
    const input: ConditionInput = { field, op };
    if (!this.needsOperand()) return input;
    if (this.mode() === 'limit') {
      if (this.limitKey()) input.limit = this.limitKey() ?? undefined;
      return input;
    }
    const value = this.currentValue();
    if (value !== undefined) input.value = value;
    return input;
  }

  private currentValue(): unknown {
    const kind = this.valueKind();
    const numeric = this.choice()?.type === FIELD_TYPES.Money
      || this.choice()?.type === FIELD_TYPES.Number
      || this.choice()?.type === FIELD_TYPES.Lookup;
    switch (kind) {
      case 'money':
      case 'number':
        return this.numberValue() ?? undefined;
      case 'yesno':
        return this.boolValue() ?? undefined;
      case 'multiselect':
        return this.listValue().length > 0 ? this.listValue() : undefined;
      case 'list': {
        const parts = this.textValue()
          .split(',')
          .map((p) => p.trim())
          .filter((p) => p !== '');
        if (parts.length === 0) return undefined;
        return numeric ? parts.map((p) => (Number.isNaN(Number(p)) ? p : Number(p))) : parts;
      }
      default: {
        const text = this.textValue().trim();
        return text === '' ? undefined : text;
      }
    }
  }

  protected saveCondition(clear: boolean): void {
    const c = this.config();
    const step = this.editing();
    if (!c || !step) return;
    this.busy.set(true);
    this.errors.set({});
    this.generalError.set(null);
    this.api.saveCondition(c.code, step.key, c.version, clear ? null : this.buildCondition()).subscribe({
      next: (updated) => {
        this.replaceModule(updated);
        this.busy.set(false);
        this.dialogOpen.set(false);
        this.notifications.success(
          clear
            ? `${step.name} is now always required. ${updated.name} is now version ${updated.version}.`
            : `Condition saved. ${updated.name} is now version ${updated.version}.`,
        );
      },
      error: (err: unknown) => {
        this.busy.set(false);
        if (this.isConflict(err)) {
          this.dialogOpen.set(false);
          return;
        }
        if (err instanceof ApiError && err.fieldErrors.length > 0) {
          const next: Record<string, string> = {};
          const rest: string[] = [];
          for (const f of err.fieldErrors) {
            const key = f.field.toLowerCase();
            if (key.startsWith('condition.')) next[key] = f.message;
            else rest.push(f.message);
          }
          this.errors.set(next);
          this.generalError.set(rest.length > 0 ? rest.join(' ') : Object.keys(next).length === 0 ? userMessage(err) : null);
          return;
        }
        this.generalError.set(userMessage(err));
      },
    });
  }

  // ------------------------------------------------------------------ helpers

  private isConflict(err: unknown): boolean {
    if (err instanceof ApiError && err.code === STATE_CONFLICT_CODE) {
      this.conflict.set(true);
      return true;
    }
    return false;
  }

  private replaceModule(updated: ModuleConfig): void {
    this.modules.update((list) => list.map((m) => (m.code === updated.code ? updated : m)));
    this.resetDraft();
  }

  private resetDraft(): void {
    const next: Record<string, number | null> = {};
    for (const l of this.config()?.limits ?? []) next[`${l.stepKey}__${l.limitKey}`] = l.valueMinor / 100;
    this.draft.set(next);
  }
}

function toMinor(rupees: number): number {
  return Math.round(rupees * 100);
}
