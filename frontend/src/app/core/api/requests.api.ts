import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';
import {
  ActionRequest,
  AuditEventDto,
  CreateRequestBody,
  DashboardSummary,
  InboxQuery,
  MineQuery,
  Paged,
  RequestAction,
  RequestDetail,
  RequestListItem,
} from './models';

@Injectable({ providedIn: 'root' })
export class RequestsApi {
  private readonly api = inject(ApiClient);

  create(body: CreateRequestBody): Observable<RequestDetail> {
    return this.api.post<RequestDetail>('/api/requests', body);
  }

  get(id: number): Observable<RequestDetail> {
    return this.api.get<RequestDetail>(`/api/requests/${id}`);
  }

  /** The comment is sent only for Reject and Cancel; captured values only for Complete. */
  act(
    id: number,
    action: RequestAction,
    rowVersion: number,
    options: { comment?: string | null; captured?: Record<string, unknown> | null } = {},
  ): Observable<RequestDetail> {
    const body: ActionRequest = { action, rowVersion };
    if (action === 'Reject' || action === 'Cancel') {
      body.comment = options.comment ?? null;
    }
    if (action === 'Complete' && options.captured) {
      body.captured = options.captured;
    }
    return this.api.post<RequestDetail>(`/api/requests/${id}/actions`, body);
  }

  audit(id: number): Observable<AuditEventDto[]> {
    return this.api.get<AuditEventDto[]>(`/api/requests/${id}/audit`);
  }

  mine(query: MineQuery = {}): Observable<Paged<RequestListItem>> {
    return this.list('/api/requests/mine', query);
  }

  /** Every request in the organisation; the server allows only Admin and SystemAdmin. */
  all(query: MineQuery = {}): Observable<Paged<RequestListItem>> {
    return this.list('/api/requests/all', query);
  }

  /** Requests raised by the signed-in person's direct reports. */
  team(query: MineQuery = {}): Observable<Paged<RequestListItem>> {
    return this.list('/api/requests/team', query);
  }

  private list(path: string, query: MineQuery): Observable<Paged<RequestListItem>> {
    const { status, approvalStatus, ...rest } = query;
    return this.api.get<Paged<RequestListItem>>(path, {
      ...rest,
      status,
      approvalStatus,
    });
  }

  inbox(query: InboxQuery = {}): Observable<Paged<RequestListItem>> {
    return this.api.get<Paged<RequestListItem>>('/api/requests/inbox', { ...query });
  }

  inboxCount(): Observable<{ count: number }> {
    return this.api.get<{ count: number }>('/api/requests/inbox/count');
  }

  dashboardSummary(): Observable<DashboardSummary> {
    return this.api.get<DashboardSummary>('/api/dashboard/summary');
  }
}
