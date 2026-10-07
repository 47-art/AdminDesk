import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';
import { PageQuery, Paged, TeamRow } from './models';

@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly api = inject(ApiClient);

  team(query: PageQuery = {}): Observable<Paged<TeamRow>> {
    return this.api.get<Paged<TeamRow>>('/api/team', { ...query });
  }
}
