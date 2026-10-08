using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AdminDesk.Infrastructure.Identity;

// Used for the user, role and claim tables only. The schema itself is created by the
// numbered SQL script, not by this context.
public class AppIdentityDbContext : IdentityDbContext<AppUser, IdentityRole, string>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : base(options)
    {
    }
}
