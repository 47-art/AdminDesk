using Microsoft.AspNetCore.Identity;

namespace AdminDesk.Infrastructure.Identity;

public class AppUser : IdentityUser
{
    // The linked employee; empty for accounts that are not tied to a person (the bootstrap administrator).
    public long? EmployeeId { get; set; }
}
