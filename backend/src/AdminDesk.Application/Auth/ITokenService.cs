namespace AdminDesk.Application.Auth;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) Issue(AuthenticatedUser user);
}
