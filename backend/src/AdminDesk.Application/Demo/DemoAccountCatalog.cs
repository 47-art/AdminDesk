using AdminDesk.SharedKernel.Constants;

namespace AdminDesk.Application.Demo;

public sealed record DemoAccount(string Role, string Name, string Email, string EmployeeCode, string Description);

// The ten fixed demo people, one per role. They all share ConfigKeys.DemoPassword
// and are present only when demo mode is on.
public static class DemoAccountCatalog
{
    // The non-demo top of the reporting tree; has no login account.
    public const string TopEmployeeCode = "E0011";

    public static readonly IReadOnlyList<DemoAccount> Accounts = new[]
    {
        new DemoAccount(Roles.Employee, "Priya Nair", "employee@demo.test", "E0010",
            "Raises requests and follows them through to closure."),
        new DemoAccount(Roles.Manager, "Rohan Kapoor", "manager@demo.test", "E0009",
            "Approves requests raised by direct reports and sees them under Team requests."),
        new DemoAccount(Roles.Admin, "Rahul Verma", "admin@demo.test", "E0005",
            "Verifies stationery requests, handles courier dispatch, can reject any in-progress request, and sees every request and the audit trail."),
        new DemoAccount(Roles.Finance, "Anita Desai", "finance@demo.test", "E0002",
            "Raises and follows own requests; no steps are assigned to Finance in the current modules."),
        new DemoAccount(Roles.HR, "Suresh Iyer", "hr@demo.test", "E0003",
            "Raises and follows own requests, and sees the whole team directory."),
        new DemoAccount(Roles.IT, "Kavita Shah", "it@demo.test", "E0004",
            "Raises and follows own requests; no steps are assigned to IT in the current modules."),
        new DemoAccount(Roles.Store, "Meera Joshi", "store@demo.test", "E0006",
            "Verifies stationery requests, confirms stock, issues the items and confirms the stock update."),
        new DemoAccount(Roles.Security, "Imran Sheikh", "security@demo.test", "E0007",
            "Raises and follows own requests; no steps are assigned to Security in the current modules."),
        new DemoAccount(Roles.Management, "Vikram Rao", "management@demo.test", "E0001",
            "Sees every request and the audit trail read-only; approves high-value requests as limits are configured."),
        new DemoAccount(Roles.SystemAdmin, "Neha Kulkarni", "sysadmin@demo.test", "E0008",
            "Technical role: configures workflow definitions, limits and background jobs as those screens arrive, with a read-only view of requests."),
    };
}
