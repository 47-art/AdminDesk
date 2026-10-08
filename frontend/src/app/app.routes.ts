import { Routes } from '@angular/router';

import { authGuard, employeeProfileGuard, roleGuard } from './core/auth/guards';
import { ROLE_GROUPS } from './core/constants/roles';
import { ROUTE_PATHS } from './core/constants/routes';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  {
    path: ROUTE_PATHS.Login,
    loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: ROUTE_PATHS.Dashboard },
      {
        path: ROUTE_PATHS.Dashboard,
        loadChildren: () => import('./features/dashboard/dashboard.routes').then((m) => m.DASHBOARD_ROUTES),
      },
      {
        path: ROUTE_PATHS.Inbox,
        loadChildren: () => import('./features/inbox/inbox.routes').then((m) => m.INBOX_ROUTES),
      },
      {
        path: ROUTE_PATHS.MyRequests,
        canActivate: [employeeProfileGuard],
        loadChildren: () => import('./features/my-requests/my-requests.routes').then((m) => m.MY_REQUESTS_ROUTES),
      },
      {
        path: ROUTE_PATHS.AllRequests,
        canActivate: [roleGuard(ROLE_GROUPS.OrganisationWide)],
        loadChildren: () => import('./features/my-requests/my-requests.routes').then((m) => m.ALL_REQUESTS_ROUTES),
      },
      {
        path: ROUTE_PATHS.TeamRequests,
        canActivate: [roleGuard(ROLE_GROUPS.RequestManagers)],
        loadChildren: () => import('./features/my-requests/my-requests.routes').then((m) => m.TEAM_REQUESTS_ROUTES),
      },
      {
        path: ROUTE_PATHS.NewRequest,
        canActivate: [employeeProfileGuard],
        loadChildren: () => import('./features/new-request/new-request.routes').then((m) => m.NEW_REQUEST_ROUTES),
      },
      {
        path: `${ROUTE_PATHS.RequestDetail}/:id`,
        loadChildren: () =>
          import('./features/request-detail/request-detail.routes').then((m) => m.REQUEST_DETAIL_ROUTES),
      },
      {
        path: ROUTE_PATHS.Team,
        canActivate: [roleGuard(ROLE_GROUPS.Team)],
        loadChildren: () => import('./features/admin-pages/admin-pages.routes').then((m) => m.TEAM_ROUTES),
      },
      {
        path: ROUTE_PATHS.Forbidden,
        loadComponent: () => import('./features/shared-pages/forbidden.page').then((m) => m.ForbiddenPage),
      },
      {
        path: ROUTE_PATHS.NotFound,
        loadComponent: () => import('./features/shared-pages/not-found.page').then((m) => m.NotFoundPage),
      },
      { path: '**', redirectTo: ROUTE_PATHS.NotFound },
    ],
  },
];
