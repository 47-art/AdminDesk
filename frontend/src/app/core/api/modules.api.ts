import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';
import { ModuleDefinitionDto, ModuleSummary } from './models';

@Injectable({ providedIn: 'root' })
export class ModulesApi {
  private readonly api = inject(ApiClient);

  list(): Observable<ModuleSummary[]> {
    return this.api.get<ModuleSummary[]>('/api/modules');
  }

  get(code: string): Observable<ModuleDefinitionDto> {
    return this.api.get<ModuleDefinitionDto>(`/api/modules/${encodeURIComponent(code)}`);
  }
}
