import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';
import { LoginResponse, MeResponse, PublicConfig } from './models';

export const LOGIN_URL = '/api/auth/login';

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly api = inject(ApiClient);

  login(email: string, password: string): Observable<LoginResponse> {
    return this.api.post<LoginResponse>(LOGIN_URL, { email, password });
  }

  me(): Observable<MeResponse> {
    return this.api.get<MeResponse>('/api/me');
  }

  publicConfig(): Observable<PublicConfig> {
    return this.api.get<PublicConfig>('/api/config/public');
  }
}
