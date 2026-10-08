using AdminDesk.Application.Auth;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

[ApiController]
[Route(ApiRoutes.Auth)]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ITokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService auth, ITokenService tokens, ILogger<AuthController> logger)
    {
        _auth = auth;
        _tokens = tokens;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        AuthenticatedUser user;
        try
        {
            user = await _auth.LoginAsync(request.Email.Trim(), request.Password, ct);
        }
        catch (AppException ex)
        {
            // The email only; the password is never logged.
            _logger.LogInformation("Sign-in failed for {Email}: {Code}", request.Email, ex.Code);
            throw;
        }

        var (token, expiresAtUtc) = _tokens.Issue(user);
        var dto = new AuthUserDto(user.Id, user.Name, user.Email, user.EmployeeId, user.Roles);
        return Ok(ApiResponse<LoginResponse>.Ok(new LoginResponse(token, expiresAtUtc, dto)));
    }
}
