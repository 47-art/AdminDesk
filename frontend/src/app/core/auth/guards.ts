import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { ROLE_GROUPS, Role } from '../constants/roles';
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

/** Keeps technical roles out of the business list and create pages; they go to the dashboard. */
export const businessAreaGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return authGuard(route, state);
  return auth.hasAnyRole(ROLE_GROUPS.TechnicalOnly) ? router.createUrlTree(['/', ROUTE_PATHS.Dashboard]) : true;
};

/** For pages about the signed-in person's own requests: accounts without an employee profile go to the dashboard. */
export const employeeProfileGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return authGuard(route, state);
  return auth.hasEmployeeProfile() ? true : router.createUrlTree(['/', ROUTE_PATHS.Dashboard]);
};
