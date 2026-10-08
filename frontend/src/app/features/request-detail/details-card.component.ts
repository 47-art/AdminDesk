import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { RequestDetail } from '../../core/api/models';
import { formatDate } from '../../shared/formatters/dates';
import { NOT_PROVIDED, formatFieldValue, isBlankValue } from '../../shared/field-value/format-field-value';

interface Row {
  label: string;
  value: string;
  muted: boolean;
}

interface Group {
  title: string | null;
  rows: Row[];
}

function row(label: string, value: string | null | undefined): Row {
  const blank = value === null || value === undefined || value.trim() === '';
  return { label, value: blank ? NOT_PROVIDED : value, muted: blank };
}

/** Read-only details of a request: the common fields first, then the module sections. */
@Component({
  selector: 'app-details-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .card {
      background: var(--p-surface-0, #ffffff);
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      padding: var(--space-lg);
    }
    h2 {
      margin: 0 0 var(--space-md);
    }
    h3 {
      margin: var(--space-lg) 0 var(--space-sm);
    }
    dl {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-md) var(--space-lg);
      margin: 0;
    }
    dt {
      font-size: 12px;
      color: var(--p-text-muted-color);
      margin-bottom: 2px;
    }
    dd {
      margin: 0;
      overflow-wrap: anywhere;
      white-space: pre-line;
    }
    dd.muted {
      color: var(--p-text-muted-color);
    }
    .wide {
      grid-column: 1 / -1;
    }
    @media (max-width: 767px) {
      dl {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
  template: `
    <section class="card" aria-labelledby="details-title">
      <h2 id="details-title" class="text-heading">Details</h2>
      @for (group of groups(); track $index) {
        @if (group.title) {
          <h3 class="text-section">{{ group.title }}</h3>
        }
        <dl>
          @for (r of group.rows; track r.label) {
            <div [class.wide]="r.value.length > 60">
              <dt>{{ r.label }}</dt>
              <dd [class.muted]="r.muted">{{ r.value }}</dd>
            </div>
          }
        </dl>
      }
    </section>
  `,
})
export class DetailsCardComponent {
  readonly detail = input.required<RequestDetail>();

  protected readonly groups = computed<Group[]>(() => {
    const d = this.detail();
    const requester = `${d.requester.name} (${d.requester.employeeCode})`;
    const common: Group = {
      title: null,
      rows: [
        row('Requester', requester),
        row('Department', d.department ?? d.requester.department),
        row('Project', d.project?.label),
        row('Location', d.location?.label),
        row('Cost centre', d.costCentre?.label),
        row('Request date', formatDate(d.requestDate)),
        row('Required date', d.requiredDate ? formatDate(d.requiredDate) : null),
        row('Priority', d.priority),
        row('Remarks', d.remarks),
      ],
    };
    const sections: Group[] = d.definition.sections.map((section) => ({
      title: section.title,
      rows: section.fields.map((field) => {
        const raw = d.payload[field.key];
        const text = formatFieldValue(field, raw, d.lookupLabels[field.key]);
        return { label: field.label, value: text, muted: isBlankValue(raw) && field.type !== 'YesNo' };
      }),
    }));
    return [common, ...sections];
  });
}
