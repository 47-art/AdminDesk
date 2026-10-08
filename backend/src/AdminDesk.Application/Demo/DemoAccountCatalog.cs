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
        new DemoAccount(Roles.Management, "Vikram Rao", "management@demo.test", "E0001",
            "Sees every request and report, approves high-value items and reviews the audit trail."),
        new DemoAccount(Roles.Finance, "Anita Desai", "finance@demo.test", "E0002",
            "Approves advances and expenses, and settles or closes them."),
        new DemoAccount(Roles.HR, "Suresh Iyer", "hr@demo.test", "E0003",
            "Reviews people-related requests and sees the whole team directory."),
        new DemoAccount(Roles.IT, "Kavita Shah", "it@demo.test", "E0004",
            "Fulfils SIM, laptop and other IT asset requests."),
        new DemoAccount(Roles.Admin, "Rahul Verma", "admin@demo.test", "E0005",
            "Runs the Admin desk: approves, assigns and actions most requests."),
        new DemoAccount(Roles.Store, "Meera Joshi", "store@demo.test", "E0006",
            "Issues stationery and stock items from the store."),
        new DemoAccount(Roles.Security, "Imran Sheikh", "security@demo.test", "E0007",
            "Handles visitor passes and gate-related requests."),
        new DemoAccount(Roles.SystemAdmin, "Neha Kulkarni", "sysadmin@demo.test", "E0008",
            "Manages users, workflow definitions, logs and background jobs."),
        new DemoAccount(Roles.Manager, "Rohan Kapoor", "manager@demo.test", "E0009",
            "Approves requests raised by direct reports and sees the team."),
        new DemoAccount(Roles.Employee, "Priya Nair", "employee@demo.test", "E0010",
            "Raises requests and follows them through to closure."),
    };
}
