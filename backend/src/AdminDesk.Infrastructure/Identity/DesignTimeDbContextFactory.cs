using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdminDesk.Infrastructure.Identity;

// Lets the EF tools build the context to print the schema script. The connection
// string is never opened.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseSqlite("Data Source=design-time-unused.db")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
