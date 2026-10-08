import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { Message } from 'primeng/message';
import { forkJoin, from, of } from 'rxjs';
import { catchError, concatMap, map, toArray } from 'rxjs/operators';

import { ApiError, userMessage } from '../../core/api/api-error';
import { AuthApi } from '../../core/api/auth.api';
import { ACCEPT_ATTRIBUTE, DocumentsApi, checkDocumentFile } from '../../core/api/documents.api';
import { MeResponse, ModuleDefinitionDto } from '../../core/api/models';
import { ModulesApi } from '../../core/api/modules.api';
import { RequestsApi } from '../../core/api/requests.api';
import { AuthService } from '../../core/auth/auth.service';
import { ROUTE_PATHS } from '../../core/constants/routes';
import { HasUnsavedChanges } from '../../core/forms/unsaved-changes.guard';
import { NotificationService } from '../../core/notifications/notification.service';
import { PageSkeletonComponent } from '../../shared/page-skeleton/page-skeleton.component';
import { DynamicFormComponent, RequesterInfo } from './dynamic-form.component';
import {
  CommonGroup,
  FieldGroup,
  applyServerErrors,
  buildCommonGroup,
  buildGroup,
  domIdFor,
  toCommon,
  toPayload,
} from './form-builder';

export const NO_EMPLOYEE_CODE = 'NO_EMPLOYEE_PROFILE';
const NO_EMPLOYEE_MESSAGE =
  'Your account is not linked to an employee record, so it cannot raise requests. Ask an administrator for help.';
const OUT_OF_DATE_MESSAGE = 'This form is out of date. Reload the page and try again.';
/** Space kept above a focused field for the sticky top bar. */
const TOP_BAR_OFFSET_PX = 72 + 16;

@Component({
  selector: 'app-request-form-page',
  imports: [RouterLink, ButtonDirective, Dialog, Message, DynamicFormComponent, PageSkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    h1 {
      margin: 0 0 var(--space-md);
    }
    .card {
      background: var(--p-surface-0, #ffffff);
      border: 1px solid var(--p-content-border-color);
      border-radius: 8px;
      padding: var(--space-lg);
    }
    .summary {
      margin-bottom: var(--space-md);
    }
    .footer {
      position: sticky;
      bottom: 0;
      display: flex;
      justify-content: flex-end;
      align-items: center;
      gap: var(--space-md);
      margin-top: var(--space-md);
      padding: var(--space-md);
      background: var(--p-surface-0, #ffffff);
      border-top: 1px solid var(--p-content-border-color);
      z-index: 5;
    }
    .leave {
      color: var(--p-primary-color);
      text-decoration: none;
      cursor: pointer;
      background: none;
      border: 0;
      font: inherit;
      padding: var(--space-sm);
    }
    .leave:focus-visible {
      outline: 2px solid var(--p-primary-color);
      outline-offset: 2px;
    }
    .attachments {
      margin-top: var(--space-lg);
    }
    .attachments h2 {
      margin: 0 0 var(--space-xs);
    }
    .hint {
      margin: 0 0 var(--space-md);
      color: var(--p-text-muted-color);
    }
    .hidden-input {
      display: none;
    }
    .problem {
      margin: var(--space-sm) 0 0;
      color: var(--p-red-700, #b91c1c);
    }
    .chosen {
      list-style: none;
      margin: var(--space-sm) 0 0;
      padding: 0;
    }
    .chosen li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      padding: var(--space-xs) 0;
      border-top: 1px solid var(--p-content-border-color);
    }
    .chosen .name {
      overflow-wrap: anywhere;
    }
    .no-employee {
      flex: 1;
    }
    .load-error {
      display: flex;
      align-items: center;
      gap: var(--space-md);
    }
    @media (max-width: 767px) {
      .footer {
        position: fixed;
        left: 0;
        right: 0;
        margin: 0;
        padding-bottom: calc(var(--space-md) + env(safe-area-inset-bottom));
      }
      .spacer {
        height: 96px;
      }
    }
  `,
  template: `
    @if (loading()) {
      <app-page-skeleton preset="form" />
    } @else if (loadFailed()) {
      <div class="load-error">
        <p-message severity="error">We could not load this request type. Try again.</p-message>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="load()">Try again</button>
        <a class="leave" [routerLink]="['/', newPath]">Back to request types</a>
      </div>
    } @else if (definition(); as def) {
      <h1 class="text-heading">{{ title() }}</h1>

      @if (summaryCount() > 0) {
        <p-message class="summary" severity="error" role="alert">
          Please fix {{ summaryCount() }} {{ summaryCount() === 1 ? 'field' : 'fields' }} below
        </p-message>
      }

      <form class="card" novalidate (submit)="$event.preventDefault()">
        <app-dynamic-form
          [sections]="def.sections"
          [group]="group()!"
          [common]="commonGroup()"
          [requester]="requester()"
          [requiredCommon]="def.requiredCommonFields ?? []"
        />
      </form>

      <section class="card attachments" aria-labelledby="attachments-title">
        <h2 id="attachments-title" class="text-section">Attachments (optional)</h2>
        <p class="hint">Files are uploaded as soon as the request is created. Up to 5 MB each.</p>
        <input
          #picker
          class="hidden-input"
          type="file"
          multiple
          tabindex="-1"
          [attr.accept]="accept"
          (change)="onFilesPicked(picker)"
        />
        <button
          pButton
          type="button"
          icon="pi pi-paperclip"
          label="Add files"
          severity="secondary"
          [outlined]="true"
          [disabled]="submitting()"
          (click)="picker.click()"
        ></button>
        @if (fileProblem(); as problem) {
          <p class="problem" role="alert">{{ problem }}</p>
        }
        @if (files().length > 0) {
          <ul class="chosen" aria-label="Chosen files" aria-live="polite">
            @for (file of files(); track $index) {
              <li>
                <span class="name">{{ file.name }}</span>
                <button
                  pButton
                  type="button"
                  icon="pi pi-times"
                  severity="secondary"
                  [text]="true"
                  [disabled]="submitting()"
                  [attr.aria-label]="'Remove ' + file.name + ' from the selection'"
                  (click)="removeFile($index)"
                ></button>
              </li>
            }
          </ul>
        }
      </section>

      <div class="footer">
        @if (noEmployee()) {
          <p-message class="no-employee" severity="warn">{{ noEmployeeMessage }}</p-message>
        }
        <button type="button" class="leave" (click)="leave()">Leave form</button>
        <button
          pButton
          type="button"
          [loading]="submitting()"
          [disabled]="submitting() || noEmployee()"
          (click)="submit()"
        >
          Submit request
        </button>
      </div>
      <div class="spacer"></div>

      <p-dialog
        header="Discard changes?"
        [modal]="true"
        [visible]="discardOpen()"
        [closable]="true"
        [closeOnEscape]="true"
        [draggable]="false"
        [style]="{ width: '480px', maxWidth: 'calc(100vw - 16px)' }"
        (onHide)="closeDiscard(false)"
      >
        <p>Your unsaved changes will be lost.</p>
        <ng-template #footer>
          <button pButton type="button" variant="text" (click)="closeDiscard(true)">Discard</button>
          <button pButton type="button" variant="text" (click)="closeDiscard(false)">Keep editing</button>
        </ng-template>
      </p-dialog>
    }
  `,
})
export class RequestFormPage implements OnInit, HasUnsavedChanges {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly modulesApi = inject(ModulesApi);
  private readonly requestsApi = inject(RequestsApi);
  private readonly documentsApi = inject(DocumentsApi);
  private readonly authApi = inject(AuthApi);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  protected readonly newPath = ROUTE_PATHS.NewRequest;
  protected readonly noEmployeeMessage = NO_EMPLOYEE_MESSAGE;

  protected readonly definition = signal<ModuleDefinitionDto | null>(null);
  protected readonly group = signal<FieldGroup | null>(null);
  protected readonly commonGroup = signal<CommonGroup | null>(null);
  protected readonly requester = signal<RequesterInfo | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly submitting = signal(false);
  protected readonly noEmployee = signal(false);
  protected readonly summaryCount = signal(0);
  protected readonly discardOpen = signal(false);
  protected readonly files = signal<File[]>([]);
  protected readonly fileProblem = signal<string | null>(null);
  protected readonly accept = ACCEPT_ATTRIBUTE;

  protected readonly title = computed(() => {
    const name = (this.definition()?.name ?? '').replace(/\s+request$/i, '').trim();
    return `New ${name} request`;
  });

  private code = '';
  private submitted = false;
  private discardResolver: ((leave: boolean) => void) | null = null;

  ngOnInit(): void {
    this.code = this.route.snapshot.paramMap.get('code') ?? '';
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    forkJoin({
      definition: this.modulesApi.get(this.code),
      me: this.authApi.me().pipe(catchError(() => of<MeResponse | null>(null))),
    }).subscribe({
      next: ({ definition, me }) => {
        const fields = definition.sections.flatMap((s) => s.fields);
        this.group.set(buildGroup(fields));
        this.commonGroup.set(buildCommonGroup());
        const user = this.auth.user();
        const name = me?.name ?? user?.name ?? '';
        this.requester.set({
          name,
          employeeCode: me?.employeeCode ?? '',
          department: me?.department ?? null,
        });
        this.noEmployee.set((me?.employeeId ?? user?.employeeId ?? null) === null);
        this.definition.set(definition);
        this.loading.set(false);
      },
      error: () => {
        this.loadFailed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected submit(): void {
    const def = this.definition();
    const group = this.group();
    const common = this.commonGroup();
    if (!def || !group || !common || this.submitting() || this.noEmployee()) return;

    group.markAllAsTouched();
    group.markAllAsDirty();
    common.markAllAsTouched();
    common.markAllAsDirty();
    if (group.invalid || common.invalid) {
      const names = [
        ...Object.entries(common.controls).filter(([, c]) => c.invalid).map(([n]) => n),
        ...Object.entries(group.controls).filter(([, c]) => c.invalid).map(([n]) => n),
      ];
      this.summaryCount.set(names.length);
      this.focusField(names[0], group);
      return;
    }
    this.summaryCount.set(0);

    const fields = def.sections.flatMap((s) => s.fields);
    const body = {
      moduleCode: def.code,
      definitionId: def.definitionId,
      common: toCommon(common),
      payload: toPayload(group, fields),
    };

    this.submitting.set(true);
    group.disable({ emitEvent: false });
    common.disable({ emitEvent: false });

    this.requestsApi.create(body).subscribe({
      next: (created) => {
        this.submitted = true;
        this.notifications.success(`Request ${created.requestNo} submitted`, 'Submitted');
        this.uploadChosen(created.id);
      },
      error: (err: unknown) => {
        group.enable({ emitEvent: false });
        common.enable({ emitEvent: false });
        this.submitting.set(false);
        this.handleError(err, group, common);
      },
    });
  }

  /** Uploads the chosen files one after another, then opens the request; failures are named in a warning. */
  private uploadChosen(requestId: number): void {
    const chosen = this.files();
    const open = (): void => {
      void this.router.navigate(['/', ROUTE_PATHS.RequestDetail, requestId]);
    };
    if (chosen.length === 0) {
      open();
      return;
    }
    from(chosen)
      .pipe(
        concatMap((file) =>
          this.documentsApi.upload(requestId, file).pipe(
            map(() => null),
            catchError(() => of(file.name)),
          ),
        ),
        toArray(),
      )
      .subscribe((results) => {
        const failed = results.filter((n): n is string => n !== null);
        if (failed.length > 0) {
          this.notifications.warn(
            `These files were not uploaded: ${failed.join(', ')}. Add them again from the request page.`,
            'Some files missing',
          );
        }
        open();
      });
  }

  protected onFilesPicked(input: HTMLInputElement): void {
    const picked = Array.from(input.files ?? []);
    input.value = '';
    const problems: string[] = [];
    const next = [...this.files()];
    for (const file of picked) {
      const issue = checkDocumentFile(file);
      if (issue) {
        problems.push(issue);
      } else if (!next.some((f) => f.name === file.name && f.size === file.size)) {
        next.push(file);
      }
    }
    this.files.set(next);
    this.fileProblem.set(problems.length > 0 ? problems.join(' ') : null);
  }

  protected removeFile(index: number): void {
    this.files.set(this.files().filter((_, i) => i !== index));
    this.fileProblem.set(null);
  }

  private handleError(err: unknown, group: FieldGroup, common: CommonGroup): void {
    if (err instanceof ApiError) {
      if (err.status === 403 && err.code === NO_EMPLOYEE_CODE) {
        this.noEmployee.set(true);
        return;
      }
      if (err.status === 400 && err.fieldErrors.length > 0) {
        const stale = err.fieldErrors.filter((e) => e.field.toLowerCase() === 'definitionid');
        if (stale.length > 0) this.notifications.error(OUT_OF_DATE_MESSAGE);
        const rest = err.fieldErrors.filter((e) => e.field.toLowerCase() !== 'definitionid');
        const result = applyServerErrors(group, common, rest);
        for (const e of result.unmatched) this.notifications.error(e.message);
        this.summaryCount.set(result.count);
        if (result.firstInvalid) this.focusField(result.firstInvalid, group);
        return;
      }
    }
    // Server and network problems are already announced by the shared interceptor.
    if (err instanceof ApiError && (err.status === 0 || err.status >= 500 || err.status === 401)) return;
    this.notifications.error(userMessage(err));
  }

  private focusField(name: string | undefined, group: FieldGroup): void {
    if (!name) return;
    const id = domIdFor(name, group);
    setTimeout(() => {
      const el = document.getElementById(id);
      if (!el) return;
      el.focus({ preventScroll: true });
      const top = el.getBoundingClientRect().top + window.scrollY - TOP_BAR_OFFSET_PX;
      window.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
    });
  }

  protected leave(): void {
    void this.router.navigate(['/', ROUTE_PATHS.NewRequest]);
  }

  /** Called by the route guard. */
  canLeave(): boolean | Promise<boolean> {
    if (this.submitted || !this.hasInput()) return true;
    return new Promise<boolean>((resolve) => {
      this.discardResolver = resolve;
      this.discardOpen.set(true);
    });
  }

  protected closeDiscard(leave: boolean): void {
    this.discardOpen.set(false);
    const resolve = this.discardResolver;
    this.discardResolver = null;
    resolve?.(leave);
  }

  private hasInput(): boolean {
    return !!(this.group()?.dirty || this.commonGroup()?.dirty || this.files().length > 0);
  }

  /** Reloading or closing the tab with typed input asks first. */
  @HostListener('window:beforeunload', ['$event'])
  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (!this.submitted && this.hasInput()) {
      event.preventDefault();
    }
  }
}
