using AdminDesk.Application.Auth;
using AdminDesk.Application.Masters;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace AdminDesk.Infrastructure.Identity;

public sealed class IdentityAuthService : IAuthService
{
    private const string InvalidMessage = "The email or password is incorrect.";

    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly IEmployeeRepository _employees;

    public IdentityAuthService(UserManager<AppUser> users, SignInManager<AppUser> signIn, IEmployeeRepository employees)
    {
        _users = users;
        _signIn = signIn;
        _employees = employees;
    }

    public async Task<AuthenticatedUser> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is null)
        {
            // Same answer as a wrong password, so accounts cannot be discovered.
            throw new AppException(ErrorCodes.INVALID_CREDENTIALS, InvalidMessage, 401);
        }

        var result = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            throw new AppException(ErrorCodes.ACCOUNT_LOCKED,
                "This account is locked after too many failed attempts. Try again in a few minutes.", 423);
        }
        if (!result.Succeeded)
        {
            throw new AppException(ErrorCodes.INVALID_CREDENTIALS, InvalidMessage, 401);
        }

        var roles = (await _users.GetRolesAsync(user)).ToList();

        // Manager is derived from the reporting line and is never stored as a role.
        if (user.EmployeeId is { } employeeId
            && !roles.Contains(Roles.Manager)
            && await _employees.HasDirectReportsAsync(employeeId, ct))
        {
            roles.Add(Roles.Manager);
        }

        var claims = await _users.GetClaimsAsync(user);
        var name = claims.FirstOrDefault(c => c.Type == Claims.Name)?.Value;
        if (string.IsNullOrWhiteSpace(name) && user.EmployeeId is { } id)
        {
            name = (await _employees.GetByIdAsync(id, ct))?.FullName;
        }
        name = string.IsNullOrWhiteSpace(name) ? user.Email ?? user.UserName ?? string.Empty : name;

        return new AuthenticatedUser(user.Id, name, user.Email ?? string.Empty, user.EmployeeId, roles);
    }
}
