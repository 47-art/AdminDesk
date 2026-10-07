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

    // Manager is derived from the reporting line, so it is never assigned directly.
    public static readonly string[] Assignable =
    {
        Employee, Admin, Finance, Management, HR, IT, Security, Store, SystemAdmin
    };
}
