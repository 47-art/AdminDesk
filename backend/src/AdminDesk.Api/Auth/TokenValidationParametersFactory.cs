using System.Text;
using AdminDesk.SharedKernel.Constants;
using Microsoft.IdentityModel.Tokens;

namespace AdminDesk.Api.Auth;

public static class TokenValidationParametersFactory
{
    public static TokenValidationParameters Create(JwtSettings settings) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidAudience = settings.Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        NameClaimType = Claims.Name,
        RoleClaimType = Claims.Role,
    };
}
