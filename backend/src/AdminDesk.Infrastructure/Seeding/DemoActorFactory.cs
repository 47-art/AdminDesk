using AdminDesk.Application.Demo;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.Infrastructure.Identity;
using AdminDesk.SharedKernel.Constants;
using Microsoft.AspNetCore.Identity;

namespace AdminDesk.Infrastructure.Seeding;

public sealed class DemoActorFactory : IDemoActorFactory
{
    private readonly UserManager<AppUser> _users;
    private readonly IEmployeeRepository _employees;

    public DemoActorFactory(UserManager<AppUser> users, IEmployeeRepository employees)
    {
        _users = users;
        _employees = employees;
    }

    public async Task<ActorContext> ForAccountAsync(string role, CancellationToken ct)
    {
        var account = DemoAccountCatalog.Accounts.FirstOrDefault(a => a.Role == role)
            ?? throw new InvalidOperationException($"No demo account for role {role}.");
        var user = await _users.FindByEmailAsync(account.Email)
            ?? throw new InvalidOperationException($"Demo user {account.Email} does not exist.");
        var employee = await _employees.GetByCodeAsync(account.EmployeeCode, ct)
            ?? throw new InvalidOperationException($"Employee {account.EmployeeCode} does not exist.");

        var roles = new HashSet<string>(StringComparer.Ordinal) { Roles.Employee };
        if (account.Role != Roles.Employee && account.Role != Roles.Manager)
        {
            roles.Add(account.Role);
        }
        if (await _employees.HasDirectReportsAsync(employee.Id, ct))
        {
            roles.Add(Roles.Manager);
        }
        return new ActorContext(user.Id, account.Name, checked((int)employee.Id), roles);
    }

    public async Task<ActorContext> ForEmployeeAsync(string employeeCode, CancellationToken ct)
    {
        var employee = await _employees.GetByCodeAsync(employeeCode, ct)
            ?? throw new InvalidOperationException($"Employee {employeeCode} does not exist.");
        var roles = new HashSet<string>(StringComparer.Ordinal) { Roles.Employee };
        return new ActorContext($"seed-{employeeCode}", employee.FullName, checked((int)employee.Id), roles);
    }
}
