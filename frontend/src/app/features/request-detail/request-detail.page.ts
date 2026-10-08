import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { ApiError } from '../../core/api/api-error';
import { FieldDto, RequestAction, RequestDetail } from '../../core/api/models';
import { RequestsApi } from '../../core/api/requests.api';
import { AuthService } from '../../core/auth/auth.service';
import { BadgeCountsService } from '../../core/state/badge-counts.service';
import { ROUTE_PATHS } from '../../core/constants/routes';
import { ActionDialogComponent, DialogAction, RequestActionRunner } from '../../shared/action-dialog/action-dialog.component';
import { PageSkeletonComponent } from '../../shared/page-skeleton/page-skeleton.component';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';
import { AuditTrailCardComponent } from './audit-trail-card.component';
import { DetailsCardComponent } from './details-card.component';
import { StatusPanelComponent } from './status-panel.component';

/** One page for every request, whatever its module. */
@Component({
  selector: 'app-request-detail-page',
  imports: [
    RouterLink,
    ButtonDirective,
    PageSkeletonComponent,
    StatusBadgeComponent,
    DetailsCardComponent,
    StatusPanelComponent,
    AuditTrailCardComponent,
    ActionDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .header {
      margin-bottom: var(--space-lg);
    }
    .back {
      display: inline-block;
      margin-bottom: var(--space-sm);
      color: var(--p-primary-color);
      text-decoration: none;
    }
    .title-row {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      flex-wrap: wrap;
    }
    .title-row h1 {
      margin: 0;
    }
    .subject {
      margin: var(--space-xs) 0 0;
      color: var(--p-text-muted-color);
      overflow-wrap: anywhere;
    }
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 360px;
      gap: var(--space-lg);
      align-items: start;
    }
    .main {
      display: flex;
      flex-direction: column;
      gap: var(--space-lg);
      min-width: 0;
    }
    .side {
      position: sticky;
      top: 72px;
    }
    .documents-slot {
      display: none;
    }
    .centered {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--space-md);
    }
    .centered h1,
    .centered p {
      margin: 0;
    }
    @media (max-width: 1199px) {
      .layout {
        grid-template-columns: minmax(0, 1fr);
      }
      .side {
        position: static;
        order: -1;
      }
    }
  `,
  template: `
    @if (loading()) {
      <app-page-skeleton preset="form" />
    } @else if (notFound()) {
      <div class="centered" role="alert">
        <h1 class="text-heading">We could not find that request</h1>
        <p>It may not exist or you may not have access to it.</p>
        <button pButton type="button" (click)="goToMine()">Go to my requests</button>
      </div>
    } @else if (failed()) {
      <div class="centered" role="alert">
        <p>We could not load this request. Try again.</p>
        <button pButton type="button" severity="secondary" [outlined]="true" (click)="load()">Try again</button>
      </div>
    } @else if (detail(); as d) {
      <header class="header">
        <a class="back" [routerLink]="['/', mineRoute]">Back</a>
        <div class="title-row">
          <h1 class="text-heading">{{ d.requestNo }}</h1>
          <app-status-badge [status]="d.currentStatus" />
        </div>
        <p class="subject">{{ d.subject }}</p>
      </header>
      <div class="layout">
        <div class="main">
          <app-details-card [detail]="d" />
          <div class="documents-slot" aria-hidden="true"></div>
          @if (showAudit()) {
            @for (version of [d.rowVersion]; track version) {
              <app-audit-trail-card [requestId]="d.id" />
            }
          }
        </div>
        <aside class="side" aria-label="Status and timeline">
          <app-status-panel [detail]="d" [busy]="busy()" (action)="onAction(d, $event)" />
        </aside>
      </div>
      <app-action-dialog
        [(visible)]="dialogVisible"
        [requestId]="d.id"
        [requestNo]="d.requestNo"
        [action]="dialogAction()"
        [rowVersion]="d.rowVersion"
        [captureFields]="dialogCaptureFields()"
        [primaryActionLabel]="d.primaryActionLabel"
        (completed)="afterChange($event)"
        (conflict)="reload()"
      />
    }
  `,
})
export class RequestDetailPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly requests = inject(RequestsApi);
  private readonly auth = inject(AuthService);
  private readonly runner = inject(RequestActionRunner);
  private readonly badges = inject(BadgeCountsService);

  protected readonly mineRoute = ROUTE_PATHS.MyRequests;
  protected readonly detail = signal<RequestDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly dialogVisible = signal(false);
  protected readonly dialogAction = signal<DialogAction>('Reject');
  protected readonly dialogCaptureFields = signal<FieldDto[]>([]);
  protected readonly showAudit = computed(() => this.auth.canSeeAudit());

  ngOnInit(): void {
    this.route.paramMap.subscribe(() => this.load());
  }

  protected load(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!Number.isInteger(id) || id <= 0) {
      this.loading.set(false);
      this.notFound.set(true);
      return;
    }
    this.loading.set(true);
    this.notFound.set(false);
    this.failed.set(false);
    this.requests.get(id).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        if (err instanceof ApiError && (err.status === 404 || err.status === 403)) {
          this.notFound.set(true);
        } else {
          this.failed.set(true);
        }
      },
    });
  }

  protected onAction(d: RequestDetail, action: RequestAction): void {
    if (this.busy()) return;
    if (action === 'Approve' || (action === 'Complete' && this.captureFieldsOf(d).length === 0)) {
      this.busy.set(true);
      this.runner.run(d.id, d.requestNo, action, d.rowVersion).subscribe((outcome) => {
        this.busy.set(false);
        if (outcome.kind === 'done') this.afterChange(outcome.detail);
        else if (outcome.kind === 'conflict') this.reload();
      });
      return;
    }
    this.dialogAction.set(action as DialogAction);
    this.dialogCaptureFields.set(action === 'Complete' ? this.captureFieldsOf(d) : []);
    this.dialogVisible.set(true);
  }

  protected afterChange(updated: RequestDetail): void {
    this.detail.set(updated);
    this.badges.refreshInbox();
  }

  protected reload(): void {
    const id = this.detail()?.id;
    if (id === undefined) return;
    this.requests.get(id).subscribe({
      next: (d) => this.detail.set(d),
      error: () => this.failed.set(true),
    });
    this.badges.refreshInbox();
  }

  private captureFieldsOf(d: RequestDetail): FieldDto[] {
    return d.steps.find((s) => s.isCurrent)?.captureFields ?? [];
  }

  protected goToMine(): void {
    void this.router.navigate(['/', ROUTE_PATHS.MyRequests]);
  }
}
