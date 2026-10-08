import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  afterRenderEffect,
  computed,
  effect,
  input,
  signal,
} from '@angular/core';
import { AbstractControl, FormControl, ReactiveFormsModule } from '@angular/forms';
import { DatePicker } from 'primeng/datepicker';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { ToggleSwitch } from 'primeng/toggleswitch';

import { FieldDto, SectionDto } from '../../core/api/models';
import { FIELD_TYPES, PRIORITIES } from '../../core/constants/field-types';
import { FieldErrorComponent } from '../../shared/field-error/field-error.component';
import { formatDate } from '../../shared/formatters/dates';
import { LookupFieldComponent } from '../../shared/lookup-field/lookup-field.component';
import { CommonGroup, FieldGroup, REMARKS_MAX_LENGTH, commonDomId, errorMessage, fieldDomId } from './form-builder';

export interface RequesterInfo {
  name: string;
  employeeCode: string;
  department: string | null;
}

interface AriaTarget {
  domId: string;
  control: AbstractControl;
  hasHelp: boolean;
}

const PRIORITY_OPTIONS = Object.values(PRIORITIES).map((p) => ({ label: p, value: p }));
const COUNTER_THRESHOLD = 0.8;
const FILTER_ABOVE_OPTIONS = 8;

/**
 * Draws a list of fields by type. Used for the request form (sections in two columns, with the
 * common fields first) and, when `captureFields` is given, as a bare single-column list for the
 * fields an action step asks for.
 */
@Component({
  selector: 'app-dynamic-form',
  imports: [
    ReactiveFormsModule,
    NgTemplateOutlet,
    InputText,
    Textarea,
    InputNumber,
    DatePicker,
    ToggleSwitch,
    Select,
    MultiSelect,
    LookupFieldComponent,
    FieldErrorComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }
    .section + .section {
      margin-top: var(--space-lg);
    }
    .section-title {
      margin: 0 0 var(--space-md);
      padding-bottom: var(--space-sm);
      font-size: 14px;
      font-weight: 600;
      border-bottom: 1px solid var(--p-content-border-color);
    }
    .grid {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--space-md);
    }
    @media (min-width: 768px) {
      .grid:not(.single) {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
      .span2 {
        grid-column: 1 / -1;
      }
    }
    .field {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }
    .label-row {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      gap: var(--space-sm);
      margin-bottom: var(--space-xs);
    }
    label {
      font-size: 12px;
      line-height: 1.4;
      font-weight: 600;
    }
    .required,
    .help,
    .counter {
      font-size: 12px;
      font-weight: 400;
      color: var(--p-text-muted-color);
    }
    .help {
      margin-top: var(--space-xs);
    }
    .inline {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      min-height: 40px;
    }
    .readonly {
      min-height: 40px;
      display: flex;
      align-items: center;
      padding: 0 var(--space-sm);
      background: var(--p-surface-50);
      border: 1px solid var(--p-content-border-color);
      border-radius: 6px;
    }
    :host ::ng-deep .full-width {
      width: 100%;
    }
    :host ::ng-deep .right-aligned input {
      text-align: right;
    }
  `,
  template: `
    <ng-template #fieldTpl let-field>
      @if (isShown(field)) {
      <div class="field" [class.span2]="spansBoth(field)">
        <div class="label-row">
          <label [for]="domId(field)"
            >{{ field.label }}
            @if (field.required) {
              <span class="required"> (required)</span>
            }
          </label>
          @if (showCounter(field)) {
            <span class="counter">{{ lengthOf(field) }} / {{ field.maxLength }}</span>
          }
        </div>

        @switch (field.type) {
          @case (FIELD_TYPES.Text) {
            <input pInputText type="text" class="full-width" [id]="domId(field)" [formControl]="ctl(field)" [attr.maxlength]="field.maxLength" />
          }
          @case (FIELD_TYPES.LongText) {
            <textarea pTextarea class="full-width" [autoResize]="true" rows="3" [id]="domId(field)" [formControl]="ctl(field)" [attr.maxlength]="field.maxLength"></textarea>
          }
          @case (FIELD_TYPES.Number) {
            <p-inputnumber
              styleClass="full-width"
              inputStyleClass="full-width"
              locale="en-IN"
              [useGrouping]="true"
              [minFractionDigits]="0"
              [maxFractionDigits]="2"
              [min]="field.min ?? undefined"
              [max]="field.max ?? undefined"
              [inputId]="domId(field)"
              [formControl]="ctl(field)"
            />
          }
          @case (FIELD_TYPES.Money) {
            <p-inputnumber
              styleClass="full-width right-aligned"
              inputStyleClass="full-width"
              mode="currency"
              currency="INR"
              locale="en-IN"
              [useGrouping]="true"
              [minFractionDigits]="2"
              [maxFractionDigits]="2"
              [min]="field.min ?? 0"
              [max]="field.max ?? undefined"
              [inputId]="domId(field)"
              [formControl]="ctl(field)"
            />
          }
          @case (FIELD_TYPES.Date) {
            <p-datepicker
              styleClass="full-width"
              inputStyleClass="full-width"
              dateFormat="dd/mm/yy"
              [showIcon]="true"
              [showButtonBar]="true"
              [readonlyInput]="false"
              appendTo="body"
              [inputId]="domId(field)"
              [formControl]="ctl(field)"
            />
          }
          @case (FIELD_TYPES.DateTime) {
            <p-datepicker
              styleClass="full-width"
              inputStyleClass="full-width"
              dateFormat="dd/mm/yy"
              [showTime]="true"
              hourFormat="24"
              [showIcon]="true"
              [readonlyInput]="false"
              appendTo="body"
              [inputId]="domId(field)"
              [formControl]="ctl(field)"
            />
          }
          @case (FIELD_TYPES.YesNo) {
            <div class="inline">
              <p-toggleswitch [inputId]="domId(field)" [formControl]="ctl(field)" />
              <span>{{ ctl(field).value === true ? 'Yes' : 'No' }}</span>
            </div>
          }
          @case (FIELD_TYPES.Select) {
            <p-select
              styleClass="full-width"
              [options]="field.options"
              optionLabel="label"
              optionValue="value"
              placeholder="Select"
              appendTo="body"
              [filter]="field.options.length > filterAbove"
              [showClear]="!field.required"
              [inputId]="domId(field)"
              [formControl]="ctl(field)"
            />
          }
          @case (FIELD_TYPES.MultiSelect) {
            <p-multiselect
              styleClass="full-width"
              [options]="field.options"
              optionLabel="label"
              optionValue="value"
              display="chip"
              placeholder="Select"
              appendTo="body"
              [filter]="true"
              [inputId]="domId(field)"
              [formControl]="ctl(field)"
            />
          }
          @case (FIELD_TYPES.Lookup) {
            <app-lookup-field
              [kind]="field.lookupKind ?? ''"
              placeholder="Type to search"
              [inputId]="domId(field)"
              [formControl]="ctl(field)"
            />
          }
          @default {
            <input pInputText type="text" class="full-width" [id]="domId(field)" [formControl]="ctl(field)" />
          }
        }

        @if (field.helpText) {
          <div class="help" [id]="domId(field) + '-help'">{{ field.helpText }}</div>
        }
        <app-field-error [id]="domId(field) + '-error'" [message]="messageFor(field)" />
      </div>
      }
    </ng-template>

    @if (isCapture()) {
      <div class="grid single">
        @for (field of captureFields(); track field.key) {
          <ng-container *ngTemplateOutlet="fieldTpl; context: { $implicit: field }" />
        }
      </div>
    } @else {
      @if (common(); as c) {
        <section class="section">
          <h3 class="section-title">Request details</h3>
          <div class="grid">
            <div class="field">
              <div class="label-row"><label for="common-requester">Requester</label></div>
              <div class="readonly" id="common-requester">{{ requester()?.name }} ({{ requester()?.employeeCode }})</div>
            </div>
            <div class="field">
              <div class="label-row"><label for="common-department">Department</label></div>
              <div class="readonly" id="common-department">{{ requester()?.department || 'Not provided' }}</div>
            </div>
            <div class="field">
              <div class="label-row"><label [for]="ids.projectId">Project</label></div>
              <app-lookup-field kind="project" placeholder="Type to search" [inputId]="ids.projectId" [formControl]="c.controls.projectId" />
              <app-field-error [id]="ids.projectId + '-error'" [message]="commonMessage('projectId')" />
            </div>
            <div class="field">
              <div class="label-row"><label [for]="ids.locationId">Location</label></div>
              <app-lookup-field kind="location" placeholder="Type to search" [inputId]="ids.locationId" [formControl]="c.controls.locationId" />
              <app-field-error [id]="ids.locationId + '-error'" [message]="commonMessage('locationId')" />
            </div>
            <div class="field">
              <div class="label-row"><label [for]="ids.costCentreId">Cost centre</label></div>
              <app-lookup-field kind="costCentre" placeholder="Type to search" [inputId]="ids.costCentreId" [formControl]="c.controls.costCentreId" />
              <app-field-error [id]="ids.costCentreId + '-error'" [message]="commonMessage('costCentreId')" />
            </div>
            <div class="field">
              <div class="label-row"><label for="common-requestDate">Request date</label></div>
              <div class="readonly" id="common-requestDate">{{ today }}</div>
            </div>
            <div class="field">
              <div class="label-row"><label [for]="ids.requiredDate">Required date</label></div>
              <p-datepicker
                styleClass="full-width"
                inputStyleClass="full-width"
                dateFormat="dd/mm/yy"
                [showIcon]="true"
                [showButtonBar]="true"
                [readonlyInput]="false"
                [minDate]="minDate"
                appendTo="body"
                [inputId]="ids.requiredDate"
                [formControl]="c.controls.requiredDate"
              />
              <app-field-error [id]="ids.requiredDate + '-error'" [message]="commonMessage('requiredDate')" />
            </div>
            <div class="field">
              <div class="label-row"><label [for]="ids.priority">Priority</label></div>
              <p-select
                styleClass="full-width"
                [options]="priorityOptions"
                optionLabel="label"
                optionValue="value"
                appendTo="body"
                [inputId]="ids.priority"
                [formControl]="c.controls.priority"
              />
              <app-field-error [id]="ids.priority + '-error'" [message]="commonMessage('priority')" />
            </div>
            <div class="field span2">
              <div class="label-row"><label [for]="ids.remarks">Remarks</label></div>
              <textarea pTextarea class="full-width" [autoResize]="true" rows="3" [attr.maxlength]="remarksMax" [id]="ids.remarks" [formControl]="c.controls.remarks"></textarea>
              <app-field-error [id]="ids.remarks + '-error'" [message]="commonMessage('remarks')" />
            </div>
          </div>
        </section>
      }
      @for (section of sections(); track section.title) {
        @if (sectionShown(section)) {
        <section class="section">
          <h3 class="section-title">{{ section.title }}</h3>
          <div class="grid">
            @for (field of section.fields; track field.key) {
              <ng-container *ngTemplateOutlet="fieldTpl; context: { $implicit: field }" />
            }
          </div>
        </section>
        }
      }
    }
  `,
})
export class DynamicFormComponent {
  /** Sections come from the module definition. Not used in capture-fields mode. */
  readonly sections = input<SectionDto[]>([]);
  /** One control per field key (definition fields, or the capture fields). */
  readonly group = input.required<FieldGroup>();
  /** The fixed fields. Leave out in capture-fields mode. */
  readonly common = input<CommonGroup | null>(null);
  readonly requester = input<RequesterInfo | null>(null);
  /** When given, only these fields are drawn, in one column, with no sections. */
  readonly captureFields = input<FieldDto[] | null>(null);

  protected readonly FIELD_TYPES = FIELD_TYPES;
  protected readonly filterAbove = FILTER_ABOVE_OPTIONS;
  protected readonly remarksMax = REMARKS_MAX_LENGTH;
  protected readonly priorityOptions = PRIORITY_OPTIONS;
  protected readonly today = formatDate(new Date());
  protected readonly minDate = new Date(new Date().setHours(0, 0, 0, 0));
  protected readonly ids = {
    projectId: commonDomId('projectId'),
    locationId: commonDomId('locationId'),
    costCentreId: commonDomId('costCentreId'),
    requiredDate: commonDomId('requiredDate'),
    priority: commonDomId('priority'),
    remarks: commonDomId('remarks'),
  };

  protected readonly isCapture = computed(() => this.captureFields() !== null);

  /** Bumped on every status or touched change so messages refresh under OnPush. */
  private readonly tick = signal(0);

  private readonly ariaTargets = computed<AriaTarget[]>(() => {
    const targets: AriaTarget[] = [];
    const group = this.group();
    const fields = this.captureFields() ?? this.sections().flatMap((s) => s.fields);
    for (const field of fields) {
      const control = group.controls[field.key];
      if (control) targets.push({ domId: fieldDomId(field.key), control, hasHelp: !!field.helpText });
    }
    const common = this.common();
    if (common && !this.isCapture()) {
      for (const [name, control] of Object.entries(common.controls)) {
        targets.push({ domId: commonDomId(name), control: control as AbstractControl, hasHelp: false });
      }
    }
    return targets;
  });

  constructor() {
    effect((onCleanup) => {
      const subscriptions = [this.group().events.subscribe(() => this.tick.update((n) => n + 1))];
      const common = this.common();
      if (common) subscriptions.push(common.events.subscribe(() => this.tick.update((n) => n + 1)));
      onCleanup(() => subscriptions.forEach((s) => s.unsubscribe()));
    });

    // The inner text boxes of the library controls carry the state for assistive technology.
    afterRenderEffect(() => {
      this.tick();
      for (const { domId, control, hasHelp } of this.ariaTargets()) {
        const el = document.getElementById(domId);
        if (!el) continue;
        const message = errorMessage(control);
        el.setAttribute('aria-invalid', message ? 'true' : 'false');
        const described = [hasHelp ? `${domId}-help` : '', message ? `${domId}-error` : ''].filter(Boolean).join(' ');
        if (described) el.setAttribute('aria-describedby', described);
        else el.removeAttribute('aria-describedby');
      }
    });
  }

  protected ctl(field: FieldDto): FormControl<unknown> {
    return this.group().controls[field.key];
  }

  /** A field with a show-when rule is drawn only while its control is enabled. */
  protected isShown(field: FieldDto): boolean {
    this.tick();
    return !field.showWhen || this.ctl(field).enabled;
  }

  protected sectionShown(section: SectionDto): boolean {
    return section.fields.some((f) => this.isShown(f));
  }

  protected domId(field: FieldDto): string {
    return fieldDomId(field.key);
  }

  protected spansBoth(field: FieldDto): boolean {
    return (
      field.fullWidth ||
      field.type === FIELD_TYPES.LongText ||
      field.type === FIELD_TYPES.MultiSelect
    );
  }

  protected lengthOf(field: FieldDto): number {
    const value = this.ctl(field).value;
    return typeof value === 'string' ? value.length : 0;
  }

  protected showCounter(field: FieldDto): boolean {
    this.tick();
    if (field.type !== FIELD_TYPES.Text || !field.maxLength) return false;
    return this.lengthOf(field) > field.maxLength * COUNTER_THRESHOLD;
  }

  protected messageFor(field: FieldDto): string | null {
    this.tick();
    return errorMessage(this.ctl(field), field);
  }

  protected commonMessage(name: keyof CommonGroup['controls']): string | null {
    this.tick();
    return errorMessage(this.common()?.controls[name]);
  }
}
