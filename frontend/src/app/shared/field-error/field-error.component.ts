import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Message under a control. The element id is meant for the control's aria-describedby. */
@Component({
  selector: 'app-field-error',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .error {
      display: flex;
      align-items: flex-start;
      gap: var(--space-xs);
      margin-top: var(--space-xs);
      font-size: 12px;
      line-height: 1.4;
      color: var(--p-inputtext-invalid-border-color);
    }
  `,
  template: `
    @if (message()) {
      <div class="error" [id]="id()" role="alert">
        <i class="pi pi-exclamation-circle" aria-hidden="true"></i>
        <span>{{ message() }}</span>
      </div>
    }
  `,
})
export class FieldErrorComponent {
  readonly id = input.required<string>();
  readonly message = input<string | null>(null);
}
