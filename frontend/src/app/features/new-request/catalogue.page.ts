import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';

import { ModulesApi } from '../../core/api/modules.api';
import { ModuleSummary } from '../../core/api/models';
import { ROUTE_PATHS } from '../../core/constants/routes';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PageSkeletonComponent } from '../../shared/page-skeleton/page-skeleton.component';

/** Display order of the categories. A category stored in a definition but missing here is listed after these. */
const CATEGORY_ORDER: readonly string[] = [
  'Travel and money',
  'Assets and equipment',
  'Facilities',
  'Procurement and vendors',
  'Tickets and helpdesk',
  'Visitors and others',
];

interface CategoryGroup {
  title: string;
  modules: ModuleSummary[];
}

@Component({
  selector: 'app-catalogue-page',
  imports: [FormsModule, RouterLink, ButtonDirective, InputText, Message, EmptyStateComponent, PageSkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    h1 {
      margin: 0 0 var(--space-md);
    }
    .search {
      width: 100%;
      max-width: 480px;
      margin-bottom: var(--space-lg);
    }
    .group {
      margin-bottom: var(--space-lg);
    }
    .group-title {
      margin: 0 0 var(--space-md);
      padding-bottom: var(--space-sm);
      font-size: 14px;
      font-weight: 600;
      border-bottom: 1px solid var(--p-content-border-color);
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 16px;
    }
    @media (max-width: 1023px) {
      .grid {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
    @media (max-width: 639px) {
      .grid {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .card {
      display: flex;
      gap: var(--space-md);
      padding: var(--space-md);
      background: var(--p-surface-0, #ffffff);
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      color: inherit;
      text-decoration: none;
    }
    .card:hover {
      border-color: var(--p-surface-300);
    }
    .card:focus-visible {
      outline: 2px solid var(--p-primary-color);
      outline-offset: 2px;
    }
    .icon {
      flex: none;
      display: flex;
      align-items: center;
      justify-content: center;
      width: 44px;
      height: 44px;
      border-radius: 50%;
      background: var(--p-surface-100);
      color: var(--p-primary-color);
      font-size: 24px;
    }
    .icon i {
      font-size: 24px;
    }
    .name {
      font-size: 14px;
      font-weight: 600;
    }
    .desc {
      margin-top: var(--space-xs);
      color: var(--p-text-muted-color);
      display: -webkit-box;
      -webkit-line-clamp: 2;
      line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .footnote {
      margin: var(--space-lg) 0 0;
      color: var(--p-text-muted-color);
    }
    .load-error {
      display: flex;
      align-items: center;
      gap: var(--space-md);
    }
  `,
  template: `
    <h1 class="text-heading">New request</h1>

    @if (loading()) {
      <app-page-skeleton preset="cards" />
    } @else if (failed()) {
      <div class="load-error">
        <p-message severity="error">We could not load request types. Try again.</p-message>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="load()">Try again</button>
      </div>
    } @else {
      <div class="search">
        <input
          pInputText
          type="search"
          class="w-full"
          style="width: 100%"
          placeholder="Search for a request type"
          aria-label="Search for a request type"
          [ngModel]="term()"
          (ngModelChange)="term.set($event)"
        />
      </div>

      @if (groups().length === 0 && term().trim()) {
        <app-empty-state
          icon="pi-search"
          [title]="'No request types match &quot;' + term().trim() + '&quot;'"
          body="Check the spelling or clear the search."
          actionLabel="Clear search"
          (action)="term.set('')"
        />
      } @else {
        @for (group of groups(); track group.title) {
          <section class="group" [attr.aria-label]="group.title">
            <h2 class="group-title">{{ group.title }}</h2>
            <div class="grid">
              @for (m of group.modules; track m.code) {
                <a class="card" [routerLink]="['/', newPath, m.code]">
                  <span class="icon"><i class="pi" [class]="m.icon" aria-hidden="true"></i></span>
                  <span>
                    <span class="name">{{ m.name }}</span>
                    <span class="desc">{{ m.description }}</span>
                  </span>
                </a>
              }
            </div>
          </section>
        }
        <p class="footnote">More request types are added as they become available.</p>
      }
    }
  `,
})
export class CataloguePage implements OnInit {
  private readonly modulesApi = inject(ModulesApi);

  protected readonly newPath = ROUTE_PATHS.NewRequest;
  protected readonly modules = signal<ModuleSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly term = signal('');

  protected readonly groups = computed<CategoryGroup[]>(() => {
    const needle = this.term().trim().toLowerCase();
    const matches = this.modules().filter(
      (m) => !needle || m.name.toLowerCase().includes(needle) || m.description.toLowerCase().includes(needle),
    );
    const byCategory = new Map<string, ModuleSummary[]>();
    for (const m of matches) {
      const list = byCategory.get(m.category) ?? [];
      list.push(m);
      byCategory.set(m.category, list);
    }
    const known = CATEGORY_ORDER.filter((c) => byCategory.has(c));
    const other = [...byCategory.keys()].filter((c) => !CATEGORY_ORDER.includes(c)).sort();
    return [...known, ...other].map((title) => ({
      title,
      modules: (byCategory.get(title) ?? []).sort((a, b) => a.name.localeCompare(b.name)),
    }));
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.modulesApi.list().subscribe({
      next: (list) => {
        this.modules.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }
}
