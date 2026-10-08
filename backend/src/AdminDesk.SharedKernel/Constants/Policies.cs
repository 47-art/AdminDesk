namespace AdminDesk.SharedKernel.Constants;

public static class Policies
{
    public const string Authenticated = "Authenticated";
    public const string SystemAdminOnly = "SystemAdminOnly";
    public const string AuditViewers = "AuditViewers";
    public const string TeamViewers = "TeamViewers";
    public const string OrganisationWide = "OrganisationWide";

    // Roles allowed to see team requests.
    public static readonly string[] TeamViewerRoles =
    {
        Roles.Manager, Roles.Admin, Roles.HR, Roles.Management, Roles.SystemAdmin
    };
}
