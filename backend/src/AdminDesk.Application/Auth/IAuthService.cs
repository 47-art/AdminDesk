namespace AdminDesk.Application.Auth;

public interface IAuthService
{
    // Throws AppException with INVALID_CREDENTIALS (401) or ACCOUNT_LOCKED (423).
    Task<AuthenticatedUser> LoginAsync(string email, string password, CancellationToken ct);
}
