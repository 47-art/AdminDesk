import { Routes } from '@angular/router';

import { MyRequestsPage } from './my-requests.page';

/** One list component serves three data sources; the route data picks which. */
export const MY_REQUESTS_ROUTES: Routes = [{ path: '', component: MyRequestsPage, data: { source: 'mine' } }];
export const ALL_REQUESTS_ROUTES: Routes = [{ path: '', component: MyRequestsPage, data: { source: 'all' } }];
export const TEAM_REQUESTS_ROUTES: Routes = [{ path: '', component: MyRequestsPage, data: { source: 'team' } }];
