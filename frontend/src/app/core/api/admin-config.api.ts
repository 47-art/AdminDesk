import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';
import { FieldDto, FieldOption } from './models';

/** A rule as stored: a group (any / all) or a single rule (field, op and either value or limit). */
export interface RuleNode {
  any: RuleNode[] | null;
  all: RuleNode[] | null;
  field: string | null;
  op: string | null;
  /** Money values are in rupees; lists are arrays. */
  value: unknown;
  limit: string | null;
}

export interface ConditionFieldChoice {
  key: string;
  label: string;
  type: string;
  lookupKind: string | null;
  operators: string[];
  allowsLimit: boolean;
  options: FieldOption[];
}

export interface ConfigStep {
  key: string;
  name: string;
  type: string;
  actorLabel: string;
  condition: RuleNode | null;
  conditionSummary: string | null;
  conditionEditable: boolean;
  conditionFields: ConditionFieldChoice[];
  limitKeys: string[];
}

export interface ConfigLimit {
  stepKey: string;
  limitKey: string;
  /** Integer minor units (paise). */
  valueMinor: number;
  unit: string | null;
  isSample: boolean;
}

export interface ModuleConfig {
  code: string;
  name: string;
  category: string;
  version: number;
  fields: FieldDto[];
  steps: ConfigStep[];
  limits: ConfigLimit[];
}

/** One rule sent by the editor: a value or a limit, never both. */
export interface ConditionInput {
  field: string;
  op: string;
  value?: unknown;
  limit?: string;
}

@Injectable({ providedIn: 'root' })
export class AdminConfigApi {
  private readonly api = inject(ApiClient);

  list(): Observable<ModuleConfig[]> {
    return this.api.get<ModuleConfig[]>('/api/admin/modules');
  }

  get(code: string): Observable<ModuleConfig> {
    return this.api.get<ModuleConfig>(`/api/admin/modules/${encodeURIComponent(code)}`);
  }

  updateLimit(code: string, stepKey: string, limitKey: string, valueMinor: number): Observable<ModuleConfig> {
    return this.api.put<ModuleConfig>(`/api/admin/modules/${encodeURIComponent(code)}/limits`, {
      stepKey,
      limitKey,
      valueMinor,
    });
  }

  /** A null condition clears the step's condition ("always required"). */
  saveCondition(
    code: string,
    stepKey: string,
    baseVersion: number,
    condition: ConditionInput | null,
  ): Observable<ModuleConfig> {
    return this.api.put<ModuleConfig>(
      `/api/admin/modules/${encodeURIComponent(code)}/steps/${encodeURIComponent(stepKey)}/condition`,
      { baseVersion, condition },
    );
  }
}
