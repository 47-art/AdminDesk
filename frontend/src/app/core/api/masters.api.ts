import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from './api-client';
import { Paged } from './models';

export const MASTER_KINDS = {
  Sims: 'sims',
  Assets: 'assets',
  IdCards: 'id-cards',
} as const;
export type MasterKind = (typeof MASTER_KINDS)[keyof typeof MASTER_KINDS];

export interface MasterQuery {
  page: number;
  pageSize: number;
  search?: string;
  status?: string;
}

export interface SimRecord {
  id: number;
  simNumber: string;
  mobileNumber: string;
  telecomOperator: string;
  plan: string;
  activationDate: string | null;
  status: string;
  monthlyCost: number;
  holderEmployeeId: number | null;
  holderName: string | null;
  holderCode: string | null;
}

export interface AssetRecord {
  id: number;
  assetTag: string;
  assetType: string;
  makeModel: string;
  serialNumber: string;
  status: string;
  condition: string | null;
  holderEmployeeId: number | null;
  holderName: string | null;
  holderCode: string | null;
}

export interface IdCardRecord {
  id: number;
  cardNumber: string;
  employeeId: number;
  employeeName: string | null;
  employeeCode: string | null;
  status: string;
  issuedDate: string;
}

export interface MasterHistoryEvent {
  id: number;
  eventType: string;
  employeeId: number | null;
  employeeName: string | null;
  employeeCode: string | null;
  requestId: number | null;
  requestNo: string | null;
  condition: string | null;
  cost: number | null;
  notes: string | null;
  eventUtc: string;
}

export interface SimFieldsInput {
  simNumber: string;
  mobileNumber: string;
  telecomOperator: string;
  plan: string;
  monthlyCost: number | null;
}

export interface AssetFieldsInput {
  assetTag: string;
  assetType: string;
  makeModel: string;
  serialNumber: string;
}

export interface IdCardFieldsInput {
  cardNumber: string;
  employeeId?: number | null;
  issuedDate: string | null;
}

export interface HoldingItem {
  type: string;
  id: number;
  label: string;
  since: string;
}

export interface Holdings {
  sims: HoldingItem[];
  assets: HoldingItem[];
  idCard: HoldingItem | null;
}

@Injectable({ providedIn: 'root' })
export class MastersApi {
  private readonly api = inject(ApiClient);

  sims(query: MasterQuery): Observable<Paged<SimRecord>> {
    return this.api.get<Paged<SimRecord>>('/api/masters/sims', { ...query });
  }

  assets(query: MasterQuery): Observable<Paged<AssetRecord>> {
    return this.api.get<Paged<AssetRecord>>('/api/masters/assets', { ...query });
  }

  idCards(query: MasterQuery): Observable<Paged<IdCardRecord>> {
    return this.api.get<Paged<IdCardRecord>>('/api/masters/id-cards', { ...query });
  }

  history(kind: MasterKind, id: number): Observable<MasterHistoryEvent[]> {
    return this.api.get<MasterHistoryEvent[]>(`/api/masters/${kind}/${id}/history`);
  }

  add<T>(kind: MasterKind, body: unknown): Observable<T> {
    return this.api.post<T>(`/api/masters/${kind}`, body);
  }

  edit<T>(kind: MasterKind, id: number, body: unknown): Observable<T> {
    return this.api.put<T>(`/api/masters/${kind}/${id}`, body);
  }

  retire(kind: MasterKind, id: number): Observable<void> {
    return this.api.post<void>(`/api/masters/${kind}/${id}/retire`);
  }

  myHoldings(): Observable<Holdings> {
    return this.api.get<Holdings>('/api/me/holdings');
  }
}
