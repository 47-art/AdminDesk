using System.Globalization;
using System.Text;
using AdminDesk.Application.Auth;
using AdminDesk.SharedKernel.Constants;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AdminDesk.Api.Auth;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _time;

    public JwtTokenService(JwtSettings settings, TimeProvider time)
    {
        _settings = settings;
        _time = time;
    }

    public (string Token, DateTime ExpiresAtUtc) Issue(AuthenticatedUser user)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var expires = now.AddHours(_settings.LifetimeHours);

        var claims = new Dictionary<string, object>
        {
            [Claims.Subject] = user.Id,
            [Claims.Name] = user.Name,
            [Claims.Email] = user.Email,
            [Claims.TokenId] = Guid.NewGuid().ToString("N"),
            [Claims.Role] = user.Roles.ToArray(),
        };
        if (user.EmployeeId is { } employeeId)
        {
            claims[Claims.EmployeeId] = employeeId.ToString(CultureInfo.InvariantCulture);
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expires);
    }
}
