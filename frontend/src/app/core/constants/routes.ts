export const ROUTE_PATHS = {
  Login: 'login',
  Dashboard: 'dashboard',
  Inbox: 'inbox',
  MyRequests: 'requests',
  AllRequests: 'all-requests',
  TeamRequests: 'team-requests',
  NewRequest: 'new',
  RequestDetail: 'request',
  Team: 'team',
  LimitsConditions: 'limits',
  ModuleDefinitions: 'definitions',
  Masters: 'masters',
  Forbidden: 'forbidden',
  NotFound: 'not-found',
} as const;

export type RoutePath = (typeof ROUTE_PATHS)[keyof typeof ROUTE_PATHS];
