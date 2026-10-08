import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterRenderEffect,
  inject,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, FormsModule, NgControl } from '@angular/forms';
import { AutoComplete, AutoCompleteCompleteEvent } from 'primeng/autocomplete';

import { LookupItem } from '../../core/api/models';
import { LookupsApi } from '../../core/api/lookups.api';

/** Wait this long after the last keystroke before asking the server. */
export const LOOKUP_DEBOUNCE_MS = 300;
export const LOOKUP_MIN_CHARS = 2;
export const LOOKUP_EMPTY_TEXT = 'No matches. Try a different name or code.';

/**
 * Autocomplete over a server-side lookup. The form value is the selected numeric id (or null).
 * `kind` is any lookup kind registered on the server.
 */
@Component({
  selector: 'app-lookup-field',
  imports: [FormsModule, AutoComplete],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }
    :host(.ng-invalid.ng-dirty) ::ng-deep .p-autocomplete-input {
      border-color: var(--p-inputtext-invalid-border-color);
    }
    .item {
      display: flex;
      flex-direction: column;
      line-height: 1.3;
    }
    .secondary {
      font-size: 12px;
      color: var(--p-text-muted-color);
    }
  `,
  template: `
    <p-autocomplete
      styleClass="w-full"
      [inputStyle]="{ width: '100%' }"
      [style]="{ width: '100%' }"
      [inputId]="inputId()"
      [placeholder]="placeholder()"
      [ariaLabel]="ariaLabel()"
      [suggestions]="suggestions()"
      [dropdown]="true"
      [forceSelection]="true"
      [minLength]="minChars"
      [delay]="debounceMs"
      [showEmptyMessage]="true"
      [emptyMessage]="emptyText"
      optionLabel="label"
      [disabled]="disabled()"
      [ngModel]="selected()"
      (ngModelChange)="onModelChange($event)"
      (completeMethod)="search($event)"
    >
      <ng-template #item let-item>
        <span class="item">
          <span>{{ item.label }}</span>
          @if (item.secondary) {
            <span class="secondary">{{ item.secondary }}</span>
          }
        </span>
      </ng-template>
    </p-autocomplete>
  `,
})
export class LookupFieldComponent implements ControlValueAccessor {
  private readonly lookups = inject(LookupsApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly control = inject(NgControl, { self: true, optional: true });

  readonly kind = input.required<string>();
  readonly placeholder = input<string>('');
  readonly inputId = input<string | undefined>(undefined);
  readonly ariaLabel = input<string | undefined>(undefined);
  /** Id of the element that holds the error text, wired to the inner text box. */
  readonly describedBy = input<string | null>(null);
  readonly invalid = input<boolean>(false);

  protected readonly minChars = LOOKUP_MIN_CHARS;
  protected readonly debounceMs = LOOKUP_DEBOUNCE_MS;
  protected readonly emptyText = LOOKUP_EMPTY_TEXT;

  protected readonly suggestions = signal<LookupItem[]>([]);
  protected readonly selected = signal<LookupItem | string | null>(null);
  protected readonly disabled = signal(false);

  private onChange: (value: number | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;
  private resolveToken = 0;
  private alive = true;

  constructor() {
    if (this.control) {
      this.control.valueAccessor = this;
    }
    this.destroyRef.onDestroy(() => (this.alive = false));
    afterRenderEffect(() => {
      const describedBy = this.describedBy();
      const invalid = this.invalid();
      const input = this.host.nativeElement.querySelector('input');
      if (!input) return;
      if (describedBy) input.setAttribute('aria-describedby', describedBy);
      else input.removeAttribute('aria-describedby');
      input.setAttribute('aria-invalid', invalid ? 'true' : 'false');
    });
  }

  writeValue(value: number | null): void {
    const token = ++this.resolveToken;
    if (value === null || value === undefined) {
      this.selected.set(null);
      return;
    }
    this.selected.set(null);
    this.lookups.getById(this.kind(), value).subscribe({
      next: (item) => {
        if (this.alive && token === this.resolveToken) this.selected.set(item);
      },
      error: () => undefined,
    });
  }

  registerOnChange(fn: (value: number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected search(event: AutoCompleteCompleteEvent): void {
    const query = (event.query ?? '').trim();
    // The dropdown button asks with an empty query; typed text needs the minimum length.
    if (query.length > 0 && query.length < LOOKUP_MIN_CHARS) {
      this.suggestions.set([]);
      return;
    }
    this.lookups.search(this.kind(), query).subscribe({
      next: (items) => this.suggestions.set(items),
      error: () => this.suggestions.set([]),
    });
  }

  protected onModelChange(value: LookupItem | string | null): void {
    this.selected.set(value);
    this.onTouched();
    if (value !== null && typeof value === 'object') {
      this.onChange(value.id);
    } else {
      // Typing again, or clearing, drops the previous choice.
      this.onChange(null);
    }
  }
}
