using System.Globalization;
using System.Security.Claims;
using AdminDesk.SharedKernel.Constants;

namespace AdminDesk.Api.Auth;

// Reads the signed-in user's identity from the validated token claims only.
public static class ClaimsPrincipalExtensions
{
    public static long? EmployeeId(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(Claims.EmployeeId)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirst(Claims.Subject)?.Value ?? string.Empty;

    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirst(Claims.Name)?.Value ?? string.Empty;

    public static IReadOnlyList<string> RoleNames(this ClaimsPrincipal user) =>
        user.FindAll(Claims.Role).Select(c => c.Value).ToList();
}
