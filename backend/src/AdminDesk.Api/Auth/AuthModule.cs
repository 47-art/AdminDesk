using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Auth;
using AdminDesk.SharedKernel.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace AdminDesk.Api.Auth;

public sealed class AuthModule : IServiceModule
{
    public int Order => 0;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Fails the start here when the key is too short or is the placeholder outside demo mode.
        var settings = JwtSettings.Load(configuration);
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<ClaimsActorContextFactory>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = TokenValidationParametersFactory.Create(settings);
            });

        services.AddAuthorization(options =>
        {
            // Anything without an explicit attribute still needs a signed-in user.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(Policies.Authenticated, p => p.RequireAuthenticatedUser());
            options.AddPolicy(Policies.SystemAdminOnly, p => p.RequireRole(Roles.SystemAdmin));
            options.AddPolicy(Policies.AdminOrSystemAdmin, p => p.RequireRole(Roles.Admin, Roles.SystemAdmin));
            options.AddPolicy(Policies.TeamViewers, p => p.RequireRole(Policies.TeamViewerRoles));
        });
    }
}
