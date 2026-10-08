import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { Skeleton } from 'primeng/skeleton';

export type SkeletonPreset = 'cards' | 'rows' | 'form';

/** Placeholder blocks that appear only when loading takes longer than 150 ms. Motion is switched off globally for reduced-motion users. */
@Component({
  selector: 'app-page-skeleton',
  imports: [Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .cards {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: var(--space-md);
    }
    .rows,
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-md);
    }
    .form-row {
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
    }
  `,
  template: `
    @if (visible()) {
      @switch (preset()) {
        @case ('cards') {
          <div class="cards" aria-hidden="true">
            @for (n of [1, 2, 3]; track n) {
              <p-skeleton width="100%" height="92px" borderRadius="8px" />
            }
          </div>
        }
        @case ('rows') {
          <div class="rows" aria-hidden="true">
            @for (n of [1, 2, 3, 4, 5, 6]; track n) {
              <p-skeleton width="100%" height="40px" />
            }
          </div>
        }
        @default {
          <div class="form" aria-hidden="true">
            <p-skeleton width="40%" height="28px" />
            @for (n of [1, 2, 3, 4, 5, 6]; track n) {
              <div class="form-row">
                <p-skeleton width="30%" height="14px" />
                <p-skeleton width="100%" height="40px" />
              </div>
            }
          </div>
        }
      }
    }
  `,
})
export class PageSkeletonComponent {
  readonly preset = input<SkeletonPreset>('rows');
  protected readonly visible = signal(false);

  constructor() {
    const timer = setTimeout(() => this.visible.set(true), 150);
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));
  }
}
