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
  AuditViewers: [ROLES.Admin, ROLES.SystemAdmin],
} as const satisfies Record<string, readonly Role[]>;
