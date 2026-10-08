import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

/** Centred message for a list or page with nothing to show. At most one action button. */
@Component({
  selector: 'app-empty-state',
  imports: [ButtonDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      padding: var(--space-2xl) var(--space-md);
      color: var(--p-text-muted-color);
    }
    .icon {
      font-size: 48px;
      color: var(--p-text-muted-color);
      margin-bottom: var(--space-md);
    }
    .title {
      margin: 0 0 var(--space-xs);
      color: var(--p-text-color);
    }
    .body {
      margin: 0 0 var(--space-md);
      max-width: 60ch;
    }
  `,
  template: `
    <div class="empty">
      @if (icon()) {
        <i class="pi icon" [class]="icon()" aria-hidden="true"></i>
      }
      <h2 class="title text-heading">{{ title() }}</h2>
      @if (body()) {
        <p class="body">{{ body() }}</p>
      }
      @if (actionLabel()) {
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="action.emit()">
          {{ actionLabel() }}
        </button>
      }
    </div>
  `,
})
export class EmptyStateComponent {
  readonly icon = input<string>('');
  readonly title = input.required<string>();
  readonly body = input<string>('');
  readonly actionLabel = input<string>('');
  readonly action = output<void>();
}
