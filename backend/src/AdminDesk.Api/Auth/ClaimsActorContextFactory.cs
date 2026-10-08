using System.Security.Claims;
using AdminDesk.Application.Engine;

namespace AdminDesk.Api.Auth;

// Builds the engine's actor from the validated token. The employee id stays null when the
// token has none (a sign-in with no employee record).
public sealed class ClaimsActorContextFactory
{
    public ActorContext Create(ClaimsPrincipal user)
    {
        int? employeeId = user.EmployeeId() is { } id ? checked((int)id) : null;
        var roles = new HashSet<string>(user.RoleNames(), StringComparer.Ordinal);
        return new ActorContext(user.UserId(), user.DisplayName(), employeeId, roles);
    }
}
