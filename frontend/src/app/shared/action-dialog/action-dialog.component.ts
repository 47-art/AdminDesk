import { ChangeDetectionStrategy, Component, Injectable, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { Textarea } from 'primeng/textarea';

import { ApiError, userMessage } from '../../core/api/api-error';
import { FieldDto, RequestAction, RequestDetail } from '../../core/api/models';
import { RequestsApi } from '../../core/api/requests.api';
import { NotificationService } from '../../core/notifications/notification.service';
import { DynamicFormComponent } from '../../features/new-request/dynamic-form.component';
import { FieldGroup, applyServerErrors, buildGroup, toPayload } from '../../features/new-request/form-builder';
import { FieldErrorComponent } from '../field-error/field-error.component';

export const STATE_CONFLICT_CODE = 'STATE_CONFLICT';
export const COMMENT_MAX_LENGTH = 1000;
export const CONFLICT_MESSAGE = 'This request has already been actioned by someone else. The page has been refreshed.';

export type DialogAction = Extract<RequestAction, 'Reject' | 'Cancel' | 'Complete'>;

export type ActionOutcome =
  | { kind: 'done'; detail: RequestDetail }
  | { kind: 'conflict' }
  | { kind: 'failed' };

/** Runs the one-click actions (Approve, and Complete at a step that asks for nothing). */
@Injectable({ providedIn: 'root' })
export class RequestActionRunner {
  private readonly api = inject(RequestsApi);
  private readonly notifications = inject(NotificationService);

  run(
    requestId: number,
    requestNo: string,
    action: Extract<RequestAction, 'Approve' | 'Complete'>,
    rowVersion: number,
  ): Observable<ActionOutcome> {
    return this.api.act(requestId, action, rowVersion).pipe(
      map((detail): ActionOutcome => {
        this.notifications.success(action === 'Approve' ? `${requestNo} approved` : `Step completed for ${requestNo}`);
        return { kind: 'done', detail };
      }),
      catchError((err: unknown) => {
        if (err instanceof ApiError && err.code === STATE_CONFLICT_CODE) {
          this.notifications.error(CONFLICT_MESSAGE);
          return of<ActionOutcome>({ kind: 'conflict' });
        }
        if (!(err instanceof ApiError) || (err.status !== 0 && err.status < 500 && err.status !== 401 && err.status !== 403)) {
          this.notifications.error(userMessage(err));
        }
        return of<ActionOutcome>({ kind: 'failed' });
      }),
    );
  }
}

const REASON_ERRORS: Record<'Reject' | 'Cancel', string> = {
  Reject: 'Enter a reason so the requester knows why.',
  Cancel: 'Give a reason for cancelling.',
};

/** Dialog for Reject and Cancel (a required reason) and for Complete at a step that captures fields. */
@Component({
  selector: 'app-action-dialog',
  imports: [ReactiveFormsModule, Dialog, ButtonDirective, Textarea, DynamicFormComponent, FieldErrorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host ::ng-deep .action-dialog {
      width: 480px;
      max-width: calc(100vw - 16px);
    }
    .body {
      margin: 0 0 var(--space-md);
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
    }
    textarea {
      width: 100%;
    }
    .counter {
      align-self: flex-end;
      font-size: 12px;
      color: var(--p-text-muted-color);
    }
  `,
  template: `
    <p-dialog
      [visible]="visible()"
      (visibleChange)="onVisibleChange($event)"
      [modal]="true"
      [closable]="!busy()"
      [closeOnEscape]="!busy()"
      [draggable]="false"
      [header]="title()"
      styleClass="action-dialog"
      [style]="{ width: '480px', maxWidth: 'calc(100vw - 16px)' }"
    >
      @if (isReason()) {
        @if (action() === 'Cancel') {
          <p class="body">The request will stop and cannot be restarted. You can raise a new request instead.</p>
        }
        <div class="field">
          <label for="action-reason">{{ reasonLabel() }}</label>
          <textarea
            pTextarea
            id="action-reason"
            rows="4"
            [attr.maxlength]="maxLength"
            [formControl]="reason"
            [attr.aria-describedby]="'action-reason-error'"
            [attr.aria-invalid]="reasonError() ? 'true' : null"
          ></textarea>
          <app-field-error id="action-reason-error" [message]="reasonError()" />
        </div>
      } @else {
        <app-dynamic-form [group]="captureGroup()" [captureFields]="captureFields()" />
      }
      <ng-template #footer>
        <button pButton type="button" severity="secondary" [text]="true" [disabled]="busy()" (click)="close()">
          {{ keepLabel() }}
        </button>
        @if (action() === 'Complete') {
          <button pButton type="button" [loading]="busy()" [disabled]="busy()" (click)="submit()">
            {{ confirmLabel() }}
          </button>
        } @else {
          <button
            pButton
            type="button"
            severity="danger"
            [loading]="busy()"
            [disabled]="busy() || reasonBlank()"
            (click)="submit()"
          >
            {{ confirmLabel() }}
          </button>
        }
      </ng-template>
    </p-dialog>
  `,
})
export class ActionDialogComponent {
  readonly visible = model(false);
  readonly requestId = input.required<number>();
  readonly requestNo = input.required<string>();
  readonly action = input.required<DialogAction>();
  readonly rowVersion = input.required<number>();
  readonly captureFields = input<FieldDto[]>([]);
  readonly primaryActionLabel = input<string | null>(null);

  readonly completed = output<RequestDetail>();
  readonly conflict = output<void>();

  private readonly api = inject(RequestsApi);
  private readonly notifications = inject(NotificationService);

  protected readonly maxLength = COMMENT_MAX_LENGTH;
  protected readonly reason = new FormControl('', { nonNullable: true });
  protected readonly busy = signal(false);
  protected readonly captureGroup = signal<FieldGroup>(buildGroup([]));
  private readonly reasonTick = signal(0);
  private readonly touched = signal(false);
  private opener: HTMLElement | null = null;

  protected readonly isReason = computed(() => this.action() !== 'Complete');
  protected readonly label = computed(() => this.primaryActionLabel() ?? 'Complete');
  protected readonly title = computed(() => {
    switch (this.action()) {
      case 'Reject':
        return `Reject ${this.requestNo()}?`;
      case 'Cancel':
        return `Cancel ${this.requestNo()}?`;
      default:
        return `${this.label()} for ${this.requestNo()}`;
    }
  });
  protected readonly reasonLabel = computed(() =>
    this.action() === 'Reject' ? 'Reason for rejection (required)' : 'Why are you cancelling this request?',
  );
  protected readonly confirmLabel = computed(() => {
    switch (this.action()) {
      case 'Reject':
        return 'Reject request';
      case 'Cancel':
        return 'Cancel request';
      default:
        return this.label();
    }
  });
  protected readonly keepLabel = computed(() => (this.action() === 'Complete' ? 'Keep waiting' : 'Keep request'));
  protected readonly reasonBlank = computed(() => {
    this.reasonTick();
    return this.reason.value.trim() === '';
  });
  protected readonly reasonError = computed(() => {
    this.reasonTick();
    const serverMessage = this.reason.errors?.['server'];
    if (typeof serverMessage === 'string') return serverMessage;
    if (this.touched() && this.reasonBlank() && this.isReason()) return REASON_ERRORS[this.action() as 'Reject' | 'Cancel'];
    return null;
  });

  constructor() {
    this.reason.events.subscribe(() => this.reasonTick.update((n) => n + 1));
    effect(() => {
      if (this.visible()) {
        this.opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        this.reason.reset('');
        this.touched.set(false);
        this.busy.set(false);
        this.captureGroup.set(buildGroup(this.captureFields()));
      }
    });
  }

  protected onVisibleChange(open: boolean): void {
    if (!open) this.close();
  }

  protected close(): void {
    if (this.busy()) return;
    this.visible.set(false);
    const target = this.opener;
    this.opener = null;
    if (target) setTimeout(() => target.focus());
  }

  protected submit(): void {
    if (this.busy()) return;
    const action = this.action();

    if (action === 'Complete') {
      const group = this.captureGroup();
      group.markAllAsTouched();
      if (group.invalid) return;
      this.send(action, { captured: toPayload(group, this.captureFields()) });
      return;
    }

    this.touched.set(true);
    this.reason.markAsTouched();
    if (this.reasonBlank()) return;
    this.send(action, { comment: this.reason.value.trim() });
  }

  private send(action: DialogAction, options: { comment?: string; captured?: Record<string, unknown> }): void {
    this.busy.set(true);
    this.api.act(this.requestId(), action, this.rowVersion(), options).subscribe({
      next: (detail) => {
        this.busy.set(false);
        this.notifications.success(this.successMessage(action));
        this.visible.set(false);
        this.opener = null;
        this.completed.emit(detail);
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.handleError(err, action);
      },
    });
  }

  private successMessage(action: DialogAction): string {
    switch (action) {
      case 'Reject':
        return `${this.requestNo()} rejected`;
      case 'Cancel':
        return `${this.requestNo()} cancelled`;
      default:
        return `Step completed for ${this.requestNo()}`;
    }
  }

  private handleError(err: unknown, action: DialogAction): void {
    if (err instanceof ApiError) {
      if (err.code === STATE_CONFLICT_CODE) {
        this.visible.set(false);
        this.opener = null;
        this.notifications.error(CONFLICT_MESSAGE);
        this.conflict.emit();
        return;
      }
      if (err.status === 400 && err.fieldErrors.length > 0) {
        if (action === 'Complete') {
          const result = applyServerErrors(this.captureGroup(), null, err.fieldErrors);
          for (const e of result.unmatched) this.notifications.error(e.message);
          return;
        }
        const message = err.fieldMessage('comment');
        if (message) {
          this.reason.setErrors({ server: message });
          this.reasonTick.update((n) => n + 1);
          return;
        }
      }
      if (err.status === 0 || err.status >= 500 || err.status === 401 || err.status === 403) return;
    }
    this.notifications.error(userMessage(err));
  }
}
