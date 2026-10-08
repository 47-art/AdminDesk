namespace AdminDesk.SharedKernel.Constants;

public static class Roles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Admin = "Admin";
    public const string Finance = "Finance";
    public const string Management = "Management";
    public const string HR = "HR";
    public const string IT = "IT";
    public const string Security = "Security";
    public const string Store = "Store";
    public const string SystemAdmin = "SystemAdmin";

    public static readonly string[] All =
    {
        Employee, Manager, Admin, Finance, Management, HR, IT, Security, Store, SystemAdmin
    };

    // Visibility: these roles see every request in the organisation, read-only (dashboard counters,
    // the All requests list and opening any request). Seeing a request grants no action on it.
    public static readonly string[] OrganisationWide = { Admin, SystemAdmin, Management };

    // Visibility: roles that may read the audit trail of a request.
    public static readonly string[] AuditViewers = { Admin, SystemAdmin, Management };

    // Action: only the Admin may reject an in-progress request whoever the approver is. Cancelling
    // belongs to the requester alone. Configuration is the SystemAdmin's and is separate from both.
    public static readonly string[] RequestOverride = { Admin };

    // Action: roles that edit the limits and step conditions of a module, and view the configuration.
    public static readonly string[] LimitEditors = { Management, SystemAdmin };

    // Master records: who may add, edit and retire them, and who may look at them. Holder and
    // status change only through the request flows.
    public static readonly string[] SimMasterEditors = { Admin, Management };
    public static readonly string[] AssetMasterEditors = { IT, Admin, Management };
    public static readonly string[] IdCardMasterEditors = { HR, Admin, Management };
    public static readonly string[] SimMasterViewers = { Admin, Management, SystemAdmin, IT };
    public static readonly string[] AssetMasterViewers = { IT, Admin, Management, SystemAdmin };
    public static readonly string[] IdCardMasterViewers = { HR, Admin, Management, SystemAdmin, IT };

    // Documents: these roles can open and download documents of requests they see but not upload or remove.
    public static readonly string[] DocumentReadOnly = { Management, SystemAdmin };

    // Manager is derived from the reporting line, so it is never assigned directly.
    public static readonly string[] Assignable =
    {
        Employee, Admin, Finance, Management, HR, IT, Security, Store, SystemAdmin
    };
}
