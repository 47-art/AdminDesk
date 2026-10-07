import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role } from '../constants/roles';
import { ROUTE_PATHS } from '../constants/routes';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) return true;
  const returnUrl = auth.sanitizeReturnUrl(state.url);
  return router.createUrlTree(['/', ROUTE_PATHS.Login], returnUrl ? { queryParams: { returnUrl } } : undefined);
};

/** An empty roles list means any signed-in user. */
export const roleGuard =
  (roles: readonly Role[]): CanActivateFn =>
  (route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.isAuthenticated()) {
      return authGuard(route, state);
    }
    return auth.hasAnyRole(roles) ? true : router.createUrlTree(['/', ROUTE_PATHS.Forbidden]);
  };
