using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Auth;
using AdminDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Identity;

public sealed class IdentityModule : IServiceModule
{
    public int Order => 20;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppIdentityDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DatabaseLocation>().ConnectionString));

        // Core identity only: no cookie schemes, so an unauthenticated call stays a 401.
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddSignInManager();

        services.AddScoped<IAuthService, IdentityAuthService>();

        services.AddScoped<IStartupTask, IdentityRoleSeedTask>();
        services.AddScoped<IStartupTask, DemoUserSeedTask>();
        services.AddScoped<IStartupTask, BootstrapAdminTask>();
    }
}
