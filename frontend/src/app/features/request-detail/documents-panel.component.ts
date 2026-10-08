import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';

import { ApiError, userMessage } from '../../core/api/api-error';
import { ACCEPT_ATTRIBUTE, DocumentsApi, checkDocumentFile } from '../../core/api/documents.api';
import { DocumentDto, RequestDetail } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { REQUEST_STATUSES } from '../../core/constants/statuses';
import { ROLE_GROUPS } from '../../core/constants/roles';
import { NotificationService } from '../../core/notifications/notification.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { formatDateTime } from '../../shared/formatters/dates';

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Files attached to a request: list, upload, download and remove. */
@Component({
  selector: 'app-documents-panel',
  imports: [ButtonDirective, Dialog, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .card {
      background: var(--p-surface-0, #ffffff);
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      padding: var(--space-lg);
    }
    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      flex-wrap: wrap;
      margin-bottom: var(--space-md);
    }
    h2 {
      margin: 0;
    }
    .helper {
      margin: 0 0 var(--space-md);
      padding: var(--space-sm) var(--space-md);
      background: var(--p-highlight-background, var(--p-surface-100));
      border-radius: 6px;
    }
    .problem {
      margin: 0 0 var(--space-md);
      color: var(--p-red-700, #b91c1c);
    }
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
    }
    li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      padding: var(--space-sm) 0;
      border-top: 1px solid var(--p-content-border-color);
    }
    li:first-child {
      border-top: 0;
    }
    .info {
      min-width: 0;
    }
    .name {
      overflow-wrap: anywhere;
      font-weight: 500;
    }
    .meta {
      font-size: 12px;
      color: var(--p-text-muted-color);
    }
    .actions {
      display: flex;
      gap: var(--space-xs);
      flex-shrink: 0;
    }
    .hidden-input {
      display: none;
    }
    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
      white-space: nowrap;
    }
  `,
  template: `
    <section class="card" aria-labelledby="documents-title">
      <div class="head">
        <h2 id="documents-title" class="text-heading">Documents</h2>
        @if (canUpload()) {
          <input
            #picker
            class="hidden-input"
            type="file"
            tabindex="-1"
            [attr.accept]="accept"
            (change)="onPicked(picker)"
          />
          <button
            pButton
            type="button"
            icon="pi pi-upload"
            label="Upload file"
            severity="secondary"
            [outlined]="true"
            [loading]="uploading()"
            [disabled]="uploading()"
            aria-label="Upload a document"
            (click)="picker.click()"
          ></button>
        }
      </div>

      @if (needsDocument()) {
        <p class="helper" role="note">
          <i class="pi pi-info-circle" aria-hidden="true"></i>
          This step needs a document before it can be completed.
        </p>
      }
      @if (problem(); as p) {
        <p class="problem" role="alert">{{ p }}</p>
      }

      <div aria-live="polite">
        @if (loading()) {
          <p class="meta">Loading documents...</p>
        } @else if (loadFailed()) {
          <p role="alert">We could not load the documents.</p>
          <button pButton type="button" severity="secondary" [outlined]="true" (click)="refresh()">Try again</button>
        } @else if (documents().length === 0) {
          <app-empty-state
            icon="pi-file"
            title="No documents yet"
            [body]="canUpload() ? 'Upload a file to attach it to this request.' : ''"
          />
        } @else {
          <ul aria-label="Attached documents">
            @for (doc of documents(); track doc.id) {
              <li>
                <div class="info">
                  <div class="name">{{ doc.originalName }}</div>
                  <div class="meta">
                    {{ size(doc.sizeBytes) }} · {{ doc.uploadedByName }} · {{ when(doc.uploadedUtc) }}
                    @if (stepName(doc); as step) {
                      · {{ step }}
                    }
                  </div>
                </div>
                <div class="actions">
                  <button
                    pButton
                    type="button"
                    icon="pi pi-download"
                    severity="secondary"
                    [text]="true"
                    [disabled]="busyId() === doc.id"
                    [attr.aria-label]="'Download ' + doc.originalName"
                    (click)="download(doc)"
                  ></button>
                  @if (canUpload() && doc.canRemove) {
                    <button
                      pButton
                      type="button"
                      icon="pi pi-trash"
                      severity="danger"
                      [text]="true"
                      [disabled]="busyId() === doc.id"
                      [attr.aria-label]="'Remove ' + doc.originalName"
                      (click)="askRemove(doc)"
                    ></button>
                  }
                </div>
              </li>
            }
          </ul>
        }
      </div>
      <span class="sr-only" aria-live="polite">{{ announcement() }}</span>
    </section>

    <p-dialog
      header="Remove this document?"
      [modal]="true"
      [visible]="removeTarget() !== null"
      [closable]="true"
      [closeOnEscape]="true"
      [draggable]="false"
      [style]="{ width: '440px', maxWidth: 'calc(100vw - 16px)' }"
      (onHide)="removeTarget.set(null)"
    >
      <p>{{ removeTarget()?.originalName }} will no longer be listed on this request.</p>
      <ng-template #footer>
        <button pButton type="button" variant="text" (click)="removeTarget.set(null)">Keep it</button>
        <button pButton type="button" severity="danger" (click)="confirmRemove()">Remove</button>
      </ng-template>
    </p-dialog>
  `,
})
export class DocumentsPanelComponent implements OnInit {
  private readonly api = inject(DocumentsApi);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  readonly detail = input.required<RequestDetail>();
  /** Raised after an upload or removal so the page can reload the request. */
  readonly changed = output<void>();

  protected readonly accept = ACCEPT_ATTRIBUTE;
  protected readonly documents = signal<DocumentDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly uploading = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly announcement = signal('');
  protected readonly removeTarget = signal<DocumentDto | null>(null);

  private readonly currentStep = computed(() => this.detail().steps.find((s) => s.isCurrent) ?? null);
  protected readonly needsDocument = computed(
    () => this.detail().currentStatus === REQUEST_STATUSES.InProgress && !!this.currentStep()?.requiresDocument,
  );
  protected readonly canUpload = computed(() => {
    if (this.detail().currentStatus !== REQUEST_STATUSES.InProgress) return false;
    const roles = this.auth.user()?.roles ?? [];
    const readOnly: readonly string[] = ROLE_GROUPS.ReadOnlyOnDocuments;
    return roles.some((r) => !readOnly.includes(r));
  });

  private loadedFor: number | null = null;

  constructor() {
    // Another request opened in the same page instance loads its own list.
    effect(() => {
      const id = this.detail().id;
      if (this.loadedFor !== null && this.loadedFor !== id) this.refresh();
    });
  }

  ngOnInit(): void {
    this.refresh();
  }

  protected size(bytes: number): string {
    return formatSize(bytes);
  }

  protected when(utc: string): string {
    return formatDateTime(utc);
  }

  protected stepName(doc: DocumentDto): string | null {
    if (!doc.stepKey) return null;
    return this.detail().steps.find((s) => s.key === doc.stepKey)?.name ?? null;
  }

  protected refresh(): void {
    const id = this.detail().id;
    this.loadedFor = id;
    this.loading.set(true);
    this.loadFailed.set(false);
    this.api.list(id).subscribe({
      next: (list) => {
        if (this.loadedFor !== id) return;
        this.documents.set(list);
        this.loading.set(false);
      },
      error: () => {
        if (this.loadedFor !== id) return;
        this.loading.set(false);
        this.loadFailed.set(true);
      },
    });
  }

  protected onPicked(input: HTMLInputElement): void {
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) return;
    const issue = checkDocumentFile(file);
    if (issue) {
      this.problem.set(issue);
      return;
    }
    this.problem.set(null);
    this.uploading.set(true);
    const stepKey = this.needsDocument() ? (this.currentStep()?.key ?? null) : null;
    this.api.upload(this.detail().id, file, stepKey).subscribe({
      next: () => {
        this.uploading.set(false);
        this.announcement.set(`${file.name} uploaded`);
        this.refresh();
        this.changed.emit();
      },
      error: (err: unknown) => {
        this.uploading.set(false);
        this.problem.set(this.messageFor(err));
      },
    });
  }

  protected download(doc: DocumentDto): void {
    this.busyId.set(doc.id);
    this.problem.set(null);
    this.api.download(this.detail().id, doc).subscribe({
      next: () => this.busyId.set(null),
      error: (err: unknown) => {
        this.busyId.set(null);
        this.problem.set(this.messageFor(err));
      },
    });
  }

  protected askRemove(doc: DocumentDto): void {
    this.removeTarget.set(doc);
  }

  protected confirmRemove(): void {
    const doc = this.removeTarget();
    if (!doc) return;
    this.removeTarget.set(null);
    this.busyId.set(doc.id);
    this.problem.set(null);
    this.api.remove(this.detail().id, doc.id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.announcement.set(`${doc.originalName} removed`);
        this.notifications.success(`${doc.originalName} removed`, 'Removed');
        this.refresh();
        this.changed.emit();
      },
      error: (err: unknown) => {
        this.busyId.set(null);
        this.problem.set(this.messageFor(err));
        this.refresh();
      },
    });
  }

  private messageFor(err: unknown): string {
    if (err instanceof ApiError) {
      return err.fieldMessage('file') ?? userMessage(err);
    }
    return userMessage(err);
  }
}
