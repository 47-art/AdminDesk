import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';
import { LookupItem } from './models';

@Injectable({ providedIn: 'root' })
export class LookupsApi {
  private readonly api = inject(ApiClient);

  search(kind: string, q: string, take = 20): Observable<LookupItem[]> {
    return this.api.get<LookupItem[]>(`/api/lookups/${encodeURIComponent(kind)}`, { q, take });
  }

  getById(kind: string, id: number): Observable<LookupItem> {
    return this.api.get<LookupItem>(`/api/lookups/${encodeURIComponent(kind)}/${id}`);
  }
}
