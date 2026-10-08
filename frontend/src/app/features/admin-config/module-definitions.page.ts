import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';

import { AdminConfigApi, ConfigStep, ModuleConfig } from '../../core/api/admin-config.api';
import { ModuleSummary } from '../../core/api/models';
import { ModulesApi } from '../../core/api/modules.api';
import { FIELD_TYPES } from '../../core/constants/field-types';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { formatInr } from '../../shared/formatters/money';
import { conditionSentence, limitLabel } from './condition-text';

/** Read-only view of every module's stored definition. */
@Component({
  selector: 'app-module-definitions-page',
  imports: [FormsModule, ButtonDirective, InputText, Message, Skeleton, Tag, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    h1 {
      margin: 0 0 var(--space-md);
    }
    h2 {
      margin: 0 0 var(--space-sm);
      font-size: 18px;
    }
    h3 {
      margin: 0;
      font-size: 16px;
    }
    .layout {
      display: grid;
      grid-template-columns: minmax(240px, 300px) 1fr;
      gap: var(--space-md);
      align-items: start;
    }
    @media (max-width: 800px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
    .card {
      background: var(--p-surface-0, #ffffff);
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      padding: var(--space-md);
      margin-bottom: var(--space-md);
    }
    .list {
      list-style: none;
      margin: var(--space-sm) 0 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
    }
    .item {
      display: block;
      width: 100%;
      text-align: left;
      padding: var(--space-sm);
      border: 1px solid transparent;
      border-radius: 6px;
      background: transparent;
      color: inherit;
      font: inherit;
      cursor: pointer;
    }
    .item:hover {
      background: var(--p-surface-50, #f4f8fc);
    }
    .item.active {
      background: var(--p-primary-50, #e8f1fb);
      border-color: var(--p-primary-color);
    }
    .item .meta {
      display: block;
      color: var(--p-text-muted-color);
      font-size: 13px;
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm);
      margin-bottom: var(--space-sm);
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
      vertical-align: top;
    }
    .grid th {
      font-weight: 600;
      font-size: 13px;
      color: var(--p-text-muted-color);
    }
    .scroll {
      overflow-x: auto;
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
    .full {
      width: 100%;
    }
    .muted {
      color: var(--p-text-muted-color);
    }
  `,
  template: `
    <h1 class="text-heading">Module definitions</h1>

    @if (failed()) {
      <div class="error" role="alert">
        <span>We could not load the module definitions. Try again.</span>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="load()">Try again</button>
      </div>
    } @else if (loading()) {
      <div class="skeleton-rows" aria-hidden="true">
        <p-skeleton width="100%" height="48px" />
        <p-skeleton width="100%" height="240px" />
      </div>
    } @else if (modules().length === 0) {
      <app-empty-state icon="pi-book" title="No modules loaded" body="Module definitions appear here once they are loaded." />
    } @else {
      <p-message severity="secondary" icon="pi pi-info-circle" styleClass="full">
        Loaded from the database; edit limits and conditions on the Limits and conditions page.
      </p-message>

      <div class="layout">
        <nav class="card" aria-label="Modules">
          <input pInputText type="search" class="full" placeholder="Search modules" aria-label="Search modules" [ngModel]="search()" (ngModelChange)="search.set($event ?? '')" />
          <ul class="list">
            @for (m of filtered(); track m.code) {
              <li>
                <button type="button" class="item" [class.active]="m.code === selectedCode()" [attr.aria-current]="m.code === selectedCode() ? 'true' : null" (click)="selectedCode.set(m.code)">
                  <strong>{{ m.name }}</strong>
                  <span class="meta">{{ meta(m) }}</span>
                </button>
              </li>
            } @empty {
              <li class="muted">No modules match your search.</li>
            }
          </ul>
        </nav>

        @if (config(); as c) {
          <div>
            <div class="card">
              <div class="head">
                <h2 class="text-heading">{{ c.name }}</h2>
                <p-tag severity="info" [value]="'Version ' + c.version" />
                <span class="muted">{{ c.category }}</span>
              </div>

              <h3 class="text-heading">Form fields</h3>
              <p class="note">What the person raising the request fills in.</p>
              @if (c.fields.length === 0) {
                <p class="muted">This module has no fields of its own.</p>
              } @else {
                <div class="scroll">
                  <table class="grid" aria-label="Form fields">
                    <thead>
                      <tr>
                        <th scope="col">Label</th>
                        <th scope="col">Type</th>
                        <th scope="col">Required</th>
                        <th scope="col">Choices</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (f of c.fields; track f.key) {
                        <tr>
                          <td>
                            {{ f.label }}
                            @if (f.helpText) {
                              <div class="muted">{{ f.helpText }}</div>
                            }
                          </td>
                          <td>{{ f.type }}</td>
                          <td>{{ f.required ? 'Yes' : 'No' }}</td>
                          <td>{{ choices(f) }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>

            <div class="card">
              <h3 class="text-heading">Steps in order</h3>
              <p class="note">Who acts at each step, and when the step is needed.</p>
              <div class="scroll">
                <table class="grid" aria-label="Steps">
                  <thead>
                    <tr>
                      <th scope="col">#</th>
                      <th scope="col">Step</th>
                      <th scope="col">Kind</th>
                      <th scope="col">Who acts</th>
                      <th scope="col">Condition</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (s of c.steps; track s.key; let i = $index) {
                      <tr>
                        <td>{{ i + 1 }}</td>
                        <td>{{ s.name }}</td>
                        <td>{{ s.type }}</td>
                        <td>{{ s.actorLabel }}</td>
                        <td>{{ sentence(s) }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </div>

            <div class="card">
              <h3 class="text-heading">Limits</h3>
              @if (c.limits.length === 0) {
                <p class="muted">This module has no approval limits.</p>
              } @else {
                <div class="scroll">
                  <table class="grid" aria-label="Limits">
                    <thead>
                      <tr>
                        <th scope="col">Step</th>
                        <th scope="col">Limit</th>
                        <th scope="col">Amount</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (l of c.limits; track l.stepKey + l.limitKey) {
                        <tr>
                          <td>{{ stepName(c, l.stepKey) }}</td>
                          <td>{{ label(l.limitKey) }}</td>
                          <td>{{ amount(l.valueMinor) }}{{ l.unit ? ' ' + l.unit : '' }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          </div>
        }
      </div>
    }
  `,
})
export class ModuleDefinitionsPage {
  private readonly configApi = inject(AdminConfigApi);
  private readonly modulesApi = inject(ModulesApi);

  protected readonly modules = signal<ModuleConfig[]>([]);
  private readonly summaries = signal<Record<string, ModuleSummary>>({});
  protected readonly selectedCode = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    if (!q) return this.modules();
    return this.modules().filter((m) => `${m.name} ${m.category} ${this.summaries()[m.code]?.prefix ?? ''}`.toLowerCase().includes(q));
  });

  protected readonly config = computed(() => this.modules().find((m) => m.code === this.selectedCode()) ?? null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    forkJoin({
      configs: this.configApi.list(),
      // The prefix comes from the public catalogue; the page still works without it.
      summaries: this.modulesApi.list().pipe(catchError(() => of<ModuleSummary[]>([]))),
    }).subscribe({
      next: ({ configs, summaries }) => {
        this.modules.set(configs);
        this.summaries.set(Object.fromEntries(summaries.map((s) => [s.code, s])));
        if (!configs.some((m) => m.code === this.selectedCode())) this.selectedCode.set(configs[0]?.code ?? null);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected meta(m: ModuleConfig): string {
    const prefix = this.summaries()[m.code]?.prefix;
    const parts = [m.category, prefix, `version ${m.version}`, `${m.steps.length} steps`];
    return parts.filter((p) => !!p).join(' · ');
  }

  protected choices(f: ModuleConfig['fields'][number]): string {
    if (f.options.length > 0) return f.options.map((o) => o.label).join(', ');
    if (f.type === FIELD_TYPES.Lookup && f.lookupKind) return `Lookup: ${limitLabel(f.lookupKind).toLowerCase()}`;
    return '';
  }

  protected sentence(step: ConfigStep): string {
    return conditionSentence(step);
  }

  protected label(key: string): string {
    return limitLabel(key);
  }

  protected stepName(c: ModuleConfig, stepKey: string): string {
    return c.steps.find((s) => s.key === stepKey)?.name ?? stepKey;
  }

  protected amount(valueMinor: number): string {
    return formatInr(valueMinor / 100);
  }
}
