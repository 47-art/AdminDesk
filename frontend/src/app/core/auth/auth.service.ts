import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, map, tap } from 'rxjs';
import { AuthApi } from '../api/auth.api';
import { AuthUser, LoginResponse } from '../api/models';
import { ROLE_GROUPS, Role } from '../constants/roles';
import { ROUTE_PATHS } from '../constants/routes';
import { STORAGE_KEYS } from '../constants/storage-keys';
import { NotificationService } from '../notifications/notification.service';

/** Owns the session: the token, expiry and user live in local storage only through this class. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authApi = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  readonly token = signal<string | null>(null);
  readonly user = signal<AuthUser | null>(null);
  readonly expiresAtUtc = signal<string | null>(null);

  readonly isAuthenticated = computed(() => this.token() !== null && this.user() !== null);
  readonly hasEmployeeProfile = computed(() => this.user()?.employeeId != null);
  readonly canSeeAudit = computed(() => this.hasAnyRole(ROLE_GROUPS.AuditViewers));

  /** Run once at application start. Discards a stored session that is expired or unreadable. */
  restore(): void {
    try {
      const token = localStorage.getItem(STORAGE_KEYS.accessToken);
      const expires = localStorage.getItem(STORAGE_KEYS.expiresAt);
      const rawUser = localStorage.getItem(STORAGE_KEYS.user);
      if (!token || !expires || !rawUser) {
        this.clearStorage();
        return;
      }
      const expiresMs = Date.parse(expires);
      const user = JSON.parse(rawUser) as AuthUser;
      if (Number.isNaN(expiresMs) || expiresMs <= Date.now() || !user || !Array.isArray(user.roles)) {
        this.clearStorage();
        return;
      }
      this.token.set(token);
      this.expiresAtUtc.set(expires);
      this.user.set(user);
    } catch {
      this.clearStorage();
    }
  }

  login(email: string, password: string): Observable<AuthUser> {
    return this.authApi.login(email, password).pipe(
      tap((res: LoginResponse) => {
        localStorage.setItem(STORAGE_KEYS.accessToken, res.accessToken);
        localStorage.setItem(STORAGE_KEYS.expiresAt, res.expiresAtUtc);
        localStorage.setItem(STORAGE_KEYS.user, JSON.stringify(res.user));
        this.token.set(res.accessToken);
        this.expiresAtUtc.set(res.expiresAtUtc);
        this.user.set(res.user);
      }),
      // Roles come from the login response, never from the token.
      map((res) => res.user),
    );
  }

  logout(): void {
    this.clearSession();
    void this.router.navigate(['/', ROUTE_PATHS.Login]);
  }

  /** Called when the server says the session is no longer valid. */
  expireSession(returnUrl?: string): void {
    const wasSignedIn = this.token() !== null;
    this.clearSession();
    const safe = this.sanitizeReturnUrl(returnUrl);
    void this.router.navigate(['/', ROUTE_PATHS.Login], safe ? { queryParams: { returnUrl: safe } } : undefined);
    if (wasSignedIn) {
      this.notifications.warn('Your session has ended. Sign in again.', 'Signed out');
    }
  }

  hasAnyRole(roles: readonly Role[] | readonly string[]): boolean {
    if (roles.length === 0) return this.isAuthenticated();
    const mine = this.user()?.roles ?? [];
    return roles.some((r) => mine.includes(r));
  }

  /** Accepts only app-relative paths: one leading slash, no scheme, no protocol-relative form. */
  sanitizeReturnUrl(url: string | null | undefined): string | null {
    if (!url || typeof url !== 'string') return null;
    if (!url.startsWith('/') || url.startsWith('//') || url.includes('://') || url.includes('\\')) return null;
    if (url === `/${ROUTE_PATHS.Login}` || url.startsWith(`/${ROUTE_PATHS.Login}?`)) return null;
    return url;
  }

  private clearSession(): void {
    this.clearStorage();
    this.token.set(null);
    this.user.set(null);
    this.expiresAtUtc.set(null);
  }

  private clearStorage(): void {
    localStorage.removeItem(STORAGE_KEYS.accessToken);
    localStorage.removeItem(STORAGE_KEYS.expiresAt);
    localStorage.removeItem(STORAGE_KEYS.user);
  }
}
