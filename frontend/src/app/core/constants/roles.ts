export const ROLES = {
  Employee: 'Employee',
  Manager: 'Manager',
  Admin: 'Admin',
  Finance: 'Finance',
  Management: 'Management',
  HR: 'HR',
  IT: 'IT',
  Security: 'Security',
  Store: 'Store',
  SystemAdmin: 'SystemAdmin',
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

/**
 * Which roles see which area. Used by route guards, navigation and pages so the
 * lists live in one place. Every role may use the "Waiting for me" list, so it has no group.
 */
export const ROLE_GROUPS = {
  Team: [ROLES.Manager, ROLES.Admin, ROLES.HR, ROLES.Management, ROLES.SystemAdmin],
  AuditViewers: [ROLES.Admin, ROLES.SystemAdmin, ROLES.Management],
  /** Mirrors the server list of roles that see every request; keep the two in step. */
  OrganisationWide: [ROLES.Admin, ROLES.SystemAdmin, ROLES.Management],
  /** Manager is derived from the reporting line at sign-in, so the role answers "has direct reports". */
  RequestManagers: [ROLES.Manager],
  /** Technical roles: no business menus or list pages; they keep the dashboard, People and request links. */
  TechnicalOnly: [ROLES.SystemAdmin],
  /** Roles that only read documents; they never upload or remove. */
  ReadOnlyOnDocuments: [ROLES.Management, ROLES.SystemAdmin],
  /** Mirrors the server list of roles that may reject any request; keep the two in step. */
  RequestOverride: [ROLES.Admin],
} as const satisfies Record<string, readonly Role[]>;
