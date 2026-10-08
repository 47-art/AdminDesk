using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Demo;
using AdminDesk.Application.Masters;
using System.Security.Claims;
using AdminDesk.SharedKernel.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Identity;

internal static class IdentitySeedHelper
{
    public static bool DemoEnabled(IConfiguration configuration) =>
        bool.TryParse(configuration[ConfigKeys.DemoEnabled], out var enabled) && enabled;

    public static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Code));
}

// Creates the assignable roles. Manager is derived from the reporting line, so it is not one of them.
public sealed class IdentityRoleSeedTask : IStartupTask
{
    private readonly RoleManager<IdentityRole> _roles;
    private readonly ILogger<IdentityRoleSeedTask> _logger;

    public IdentityRoleSeedTask(RoleManager<IdentityRole> roles, ILogger<IdentityRoleSeedTask> logger)
    {
        _roles = roles;
        _logger = logger;
    }

    public int Order => 20;

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var name in Roles.Assignable)
        {
            if (await _roles.RoleExistsAsync(name))
            {
                continue;
            }
            var result = await _roles.CreateAsync(new IdentityRole(name));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Role {name} could not be created: {IdentitySeedHelper.Describe(result)}");
            }
        }
        _logger.LogInformation("Identity roles are in place");
    }
}

// Creates one account per demo person, only in demo mode and only on an empty user table.
public sealed class DemoUserSeedTask : IStartupTask
{
    private readonly IConfiguration _configuration;
    private readonly UserManager<AppUser> _users;
    private readonly IEmployeeRepository _employees;
    private readonly ILogger<DemoUserSeedTask> _logger;

    public DemoUserSeedTask(IConfiguration configuration, UserManager<AppUser> users, IEmployeeRepository employees, ILogger<DemoUserSeedTask> logger)
    {
        _configuration = configuration;
        _users = users;
        _employees = employees;
        _logger = logger;
    }

    public int Order => 45;

    public async Task RunAsync(CancellationToken ct)
    {
        if (!IdentitySeedHelper.DemoEnabled(_configuration))
        {
            return;
        }
        if (await _employees.CountAsync(ct) == 0)
        {
            _logger.LogInformation("No employees yet, demo users not created");
            return;
        }
        if (await _users.Users.AnyAsync(ct))
        {
            _logger.LogInformation("Users already present, demo users not created");
            return;
        }

        var created = 0;
        foreach (var account in DemoAccountCatalog.Accounts)
        {
            var employee = await _employees.GetByCodeAsync(account.EmployeeCode, ct);
            if (employee is null)
            {
                _logger.LogWarning("Employee {Code} not found, demo account {Email} skipped", account.EmployeeCode, account.Email);
                continue;
            }

            var user = new AppUser
            {
                UserName = account.Email,
                Email = account.Email,
                EmailConfirmed = true,
                EmployeeId = employee.Id,
            };
            var result = await _users.CreateAsync(user, ConfigKeys.DemoPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Demo user {account.Email} could not be created: {IdentitySeedHelper.Describe(result)}");
            }

            // Manager is derived from direct reports, so that account holds the plain employee role only.
            var roles = new List<string> { Roles.Employee };
            if (account.Role != Roles.Employee && account.Role != Roles.Manager)
            {
                roles.Add(account.Role);
            }
            var roleResult = await _users.AddToRolesAsync(user, roles);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException($"Roles for {account.Email} could not be set: {IdentitySeedHelper.Describe(roleResult)}");
            }
            await _users.AddClaimAsync(user, new Claim(Claims.Name, account.Name));
            created++;
        }

        _logger.LogInformation("Demo users created: {Count}", created);
    }
}

// Creates the single administrator from configuration when demo mode is off and no user exists.
public sealed class BootstrapAdminTask : IStartupTask
{
    private const string DefaultName = "System Administrator";

    private readonly IConfiguration _configuration;
    private readonly UserManager<AppUser> _users;
    private readonly ILogger<BootstrapAdminTask> _logger;

    public BootstrapAdminTask(IConfiguration configuration, UserManager<AppUser> users, ILogger<BootstrapAdminTask> logger)
    {
        _configuration = configuration;
        _users = users;
        _logger = logger;
    }

    public int Order => 46;

    public async Task RunAsync(CancellationToken ct)
    {
        if (IdentitySeedHelper.DemoEnabled(_configuration))
        {
            return;
        }
        if (await _users.Users.AnyAsync(ct))
        {
            return;
        }

        var email = _configuration[ConfigKeys.BootstrapAdminEmail];
        var password = _configuration[ConfigKeys.BootstrapAdminPassword];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "Demo mode is off and no administrator is configured ({EmailKey}, {PasswordKey}); nobody can sign in until both are set.",
                ConfigKeys.BootstrapAdminEmail, ConfigKeys.BootstrapAdminPassword);
            return;
        }

        var name = _configuration[ConfigKeys.BootstrapAdminName];
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };
        var result = await _users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"The bootstrap administrator could not be created: {IdentitySeedHelper.Describe(result)}");
        }
        var roleResult = await _users.AddToRolesAsync(user, new[] { Roles.Employee, Roles.SystemAdmin });
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException($"Roles for the bootstrap administrator could not be set: {IdentitySeedHelper.Describe(roleResult)}");
        }

        var displayName = string.IsNullOrWhiteSpace(name) ? DefaultName : name;
        await _users.AddClaimAsync(user, new Claim(Claims.Name, displayName));
        _logger.LogInformation("Bootstrap administrator created ({Name})", displayName);
    }
}
