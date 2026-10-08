namespace AdminDesk.SharedKernel.Constants;

public static class Policies
{
    public const string Authenticated = "Authenticated";
    public const string SystemAdminOnly = "SystemAdminOnly";
    public const string AuditViewers = "AuditViewers";
    public const string TeamViewers = "TeamViewers";
    public const string OrganisationWide = "OrganisationWide";
    public const string LimitEditors = "LimitEditors";
    public const string ConfigViewers = "ConfigViewers";
    public const string SimMasterViewers = "SimMasterViewers";
    public const string AssetMasterViewers = "AssetMasterViewers";
    public const string IdCardMasterViewers = "IdCardMasterViewers";
    public const string SimMasterEditors = "SimMasterEditors";
    public const string AssetMasterEditors = "AssetMasterEditors";
    public const string IdCardMasterEditors = "IdCardMasterEditors";

    // Roles allowed to see team requests.
    public static readonly string[] TeamViewerRoles =
    {
        Roles.Manager, Roles.Admin, Roles.HR, Roles.Management, Roles.SystemAdmin
    };
}
