using System.Globalization;
using AdminDesk.Application.Demo;

namespace AdminDesk.Infrastructure.Seeding;

public sealed record SeedMaster(long Id, string Code, string Name);

public sealed record SeedEmployee(
    long Id,
    string Code,
    string FullName,
    string Email,
    string Designation,
    string DepartmentCode,
    string LocationCode,
    string? ManagerCode,
    int Depth);

public sealed record SampleOrganisation(
    IReadOnlyList<SeedMaster> Departments,
    IReadOnlyList<SeedMaster> Locations,
    IReadOnlyList<SeedMaster> Projects,
    IReadOnlyList<SeedMaster> CostCentres,
    IReadOnlyList<SeedEmployee> Employees);

// Builds the sample organisation from a fixed random seed, so every start produces
// the same people in the same order. Pure: it produces rows and writes nothing.
public static class SampleOrganisationGenerator
{
    public const int Seed = 20260101;
    public const int EmployeeCount = 200;

    private const string Ops = "OPS";
    private const string It = "IT";
    private const string Adm = "ADM";
    private const string Fin = "FIN";
    private const string Hr = "HR";
    private const string Head = "HO";
    private const string Works = "PW";

    private static readonly SeedMaster[] DepartmentRows =
    {
        new(1, Adm, "Administration"),
        new(2, Fin, "Finance"),
        new(3, Hr, "Human Resources"),
        new(4, It, "Information Technology"),
        new(5, Ops, "Operations"),
    };

    private static readonly SeedMaster[] LocationRows =
    {
        new(1, Head, "Mumbai Head Office"),
        new(2, Works, "Pune Works"),
    };

    private static readonly SeedMaster[] ProjectRows =
    {
        new(1, "P-1001", "Warehouse Upgrade"),
        new(2, "P-1002", "Plant Expansion"),
        new(3, "P-1003", "ERP Rollout"),
        new(4, "P-1004", "Safety Audit 2026"),
        new(5, "P-1005", "Fleet Renewal"),
        new(6, "P-1006", "Office Refresh"),
    };

    private static readonly SeedMaster[] CostCentreRows =
    {
        new(1, "CC-100", "Administration Cost Centre"),
        new(2, "CC-101", "Finance Cost Centre"),
        new(3, "CC-102", "Human Resources Cost Centre"),
        new(4, "CC-103", "Information Technology Cost Centre"),
        new(5, "CC-104", "Operations Cost Centre"),
        new(6, "CC-105", "Management Cost Centre"),
        new(7, "CC-199", "Shared Cost Centre"),
    };

    // Fixed people: code, designation, department, reports to.
    private static readonly (string Code, string Designation, string Dept, string? Manager)[] Fixed =
    {
        ("E0001", "Managing Director", Ops, "E0011"),
        ("E0002", "Head of Finance", Fin, "E0001"),
        ("E0003", "Head of Human Resources", Hr, "E0001"),
        ("E0004", "Head of Information Technology", It, "E0001"),
        ("E0005", "Head of Administration", Adm, "E0001"),
        ("E0006", "Store Keeper", Adm, "E0005"),
        ("E0007", "Security Supervisor", Adm, "E0005"),
        ("E0008", "Systems Administrator", It, "E0004"),
        ("E0009", "Operations Manager", Ops, "E0001"),
        ("E0010", "Operations Executive", Ops, "E0009"),
    };

    // Total headcount per department, fixed people included.
    private static readonly (string Dept, int Total, int Managers, string Head)[] Plan =
    {
        (Ops, 80, 2, "E0001"),
        (It, 45, 3, "E0004"),
        (Adm, 30, 2, "E0005"),
        (Fin, 25, 2, "E0002"),
        (Hr, 20, 2, "E0003"),
    };

    private static readonly string[] FirstNames =
    {
        "Aarav", "Aditi", "Aditya", "Akash", "Amit", "Ananya", "Anjali", "Arjun", "Deepak", "Divya",
        "Farhan", "Gauri", "Harsh", "Isha", "Jatin", "Kiran", "Lata", "Mahesh", "Manish", "Nikhil",
        "Nisha", "Omkar", "Pooja", "Pranav", "Rajesh", "Ritu", "Sagar", "Sanjay", "Shreya", "Sunil",
        "Tanvi", "Uday", "Varun", "Vidya", "Yash", "Zoya",
    };

    private static readonly string[] LastNames =
    {
        "Agarwal", "Bhat", "Chavan", "Deshmukh", "Fernandes", "Gaikwad", "Gupta", "Jadhav", "Kale", "Kulkarni",
        "Mehta", "More", "Naik", "Pandey", "Patil", "Pawar", "Reddy", "Sawant", "Shinde", "Singh",
        "Sharma", "Thakur", "Wagh", "Yadav",
    };

    private static readonly Dictionary<string, string[]> StaffDesignations = new()
    {
        [Ops] = new[] { "Operations Executive", "Shift Supervisor", "Logistics Coordinator", "Plant Operator", "Quality Inspector" },
        [It] = new[] { "Software Engineer", "Support Engineer", "Network Engineer", "Business Analyst", "Database Administrator" },
        [Adm] = new[] { "Admin Executive", "Front Desk Associate", "Facilities Coordinator", "Travel Desk Executive" },
        [Fin] = new[] { "Accountant", "Accounts Executive", "Payroll Analyst", "Finance Associate" },
        [Hr] = new[] { "HR Executive", "Recruiter", "Payroll Coordinator", "Learning Associate" },
    };

    public static SampleOrganisation Generate()
    {
        var demo = DemoAccountCatalog.Accounts.ToDictionary(a => a.EmployeeCode);
        var rng = new Random(Seed);

        var generatedCount = EmployeeCount - Fixed.Length - 1;
        var slots = BuildDepartmentSlots(rng, generatedCount);

        var people = new List<SeedEmployee>(EmployeeCount);
        var managerPool = Plan.ToDictionary(p => p.Dept, _ => new List<string>());
        managerPool[Ops].Add("E0009");

        // The top of the hierarchy is a non-demo chairperson.
        people.Add(Build(11, DemoAccountCatalog.TopEmployeeCode, "Ramesh Menon", "Chairperson", Adm, Head, null, 0));

        foreach (var (code, designation, dept, manager) in Fixed)
        {
            var account = demo[code];
            people.Add(new SeedEmployee(
                Number(code), code, account.Name, account.Email, designation, dept, Head, manager, 0));
        }

        var managersMade = Plan.ToDictionary(p => p.Dept, _ => 0);
        var underRohan = 0;
        for (var i = 0; i < generatedCount; i++)
        {
            var number = Fixed.Length + 2 + i;
            var code = Code(number);
            var dept = slots[i];
            var plan = Plan.First(p => p.Dept == dept);
            var name = FirstNames[rng.Next(FirstNames.Length)] + " " + LastNames[rng.Next(LastNames.Length)];
            var location = rng.NextDouble() < 0.6 ? Head : Works;

            if (managersMade[dept] < plan.Managers)
            {
                managersMade[dept]++;
                managerPool[dept].Add(code);
                people.Add(Build(number, code, name, DepartmentTitle(dept) + " Manager", dept, location, plan.Head, 0));
                continue;
            }

            string manager;
            if (dept == Ops && underRohan < 4)
            {
                manager = "E0009";
                underRohan++;
            }
            else
            {
                var pool = managerPool[dept];
                manager = pool[rng.Next(pool.Count)];
            }

            var titles = StaffDesignations[dept];
            people.Add(Build(number, code, name, titles[rng.Next(titles.Length)], dept, location, manager, 0));
        }

        return new SampleOrganisation(
            DepartmentRows, LocationRows, ProjectRows, CostCentreRows, WithDepths(people));
    }

    // Department of each generated employee, shuffled with the fixed seed.
    private static List<string> BuildDepartmentSlots(Random rng, int generatedCount)
    {
        var slots = new List<string>(generatedCount);
        foreach (var plan in Plan)
        {
            var fixedHere = Fixed.Count(f => f.Dept == plan.Dept) + (plan.Dept == Adm ? 1 : 0);
            for (var i = 0; i < plan.Total - fixedHere; i++)
            {
                slots.Add(plan.Dept);
            }
        }

        for (var i = slots.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }
        return slots;
    }

    private static SeedEmployee Build(
        int number, string code, string name, string designation, string dept, string location, string? manager, int depth)
    {
        var parts = name.Split(' ', 2);
        var email = string.Create(CultureInfo.InvariantCulture, $"{parts[0]}.{parts[1]}.{code}@example.test")
            .ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        return new SeedEmployee(number, code, name, email, designation, dept, location, manager, depth);
    }

    private static string DepartmentTitle(string dept) => dept switch
    {
        Ops => "Operations",
        It => "IT",
        Adm => "Administration",
        Fin => "Finance",
        _ => "HR",
    };

    // Orders people so that every manager comes before the people reporting to them.
    private static List<SeedEmployee> WithDepths(List<SeedEmployee> people)
    {
        var byCode = people.ToDictionary(p => p.Code);
        var depths = new Dictionary<string, int>();

        int DepthOf(string code)
        {
            if (depths.TryGetValue(code, out var known))
            {
                return known;
            }
            var manager = byCode[code].ManagerCode;
            var depth = manager is null ? 0 : DepthOf(manager) + 1;
            depths[code] = depth;
            return depth;
        }

        return people
            .Select(p => p with { Depth = DepthOf(p.Code) })
            .OrderBy(p => p.Depth)
            .ThenBy(p => p.Id)
            .ToList();
    }

    private static int Number(string code) => int.Parse(code.AsSpan(1), CultureInfo.InvariantCulture);

    private static string Code(int number) => string.Create(CultureInfo.InvariantCulture, $"E{number:D4}");
}
