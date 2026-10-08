import { ROLE_GROUPS, Role } from '../core/constants/roles';
import { ROUTE_PATHS } from '../core/constants/routes';

export interface NavItem {
  label: string;
  path: string;
  icon: string;
  /** Empty means every signed-in role. */
  roles: readonly Role[];
  /** Shows the waiting-for-me count badge. */
  showsInboxCount?: boolean;
  /** Hidden when the user holds any of these roles, even if `roles` would allow the item. */
  hiddenForRoles?: readonly Role[];
  /** Hidden for accounts that have no employee profile. */
  requiresEmployeeProfile?: boolean;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Dashboard', path: ROUTE_PATHS.Dashboard, icon: 'pi pi-home', roles: [] },
  { label: 'Waiting for me', path: ROUTE_PATHS.Inbox, icon: 'pi pi-inbox', roles: [], showsInboxCount: true, hiddenForRoles: ROLE_GROUPS.TechnicalOnly },
  { label: 'My requests', path: ROUTE_PATHS.MyRequests, icon: 'pi pi-list', roles: [], requiresEmployeeProfile: true, hiddenForRoles: ROLE_GROUPS.TechnicalOnly },
  { label: 'All requests', path: ROUTE_PATHS.AllRequests, icon: 'pi pi-list-check', roles: ROLE_GROUPS.OrganisationWide, hiddenForRoles: ROLE_GROUPS.TechnicalOnly },
  { label: 'Team requests', path: ROUTE_PATHS.TeamRequests, icon: 'pi pi-sitemap', roles: ROLE_GROUPS.RequestManagers, hiddenForRoles: ROLE_GROUPS.OrganisationWide },
  { label: 'New request', path: ROUTE_PATHS.NewRequest, icon: 'pi pi-plus-circle', roles: [], requiresEmployeeProfile: true, hiddenForRoles: ROLE_GROUPS.TechnicalOnly },
  { label: 'People', path: ROUTE_PATHS.Team, icon: 'pi pi-users', roles: ROLE_GROUPS.Team },
];
