namespace AdminDesk.Application.Auth;

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed record AuthenticatedUser(string Id, string Name, string Email, long? EmployeeId, IReadOnlyList<string> Roles);

public sealed record AuthUserDto(string Id, string Name, string Email, long? EmployeeId, IReadOnlyList<string> Roles);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, AuthUserDto User);

public sealed record DemoAccountDto(string Role, string Name, string Email, string Description);

public sealed record PublicConfigDto(bool DemoMode, string? DemoPassword, IReadOnlyList<DemoAccountDto> DemoAccounts);

public sealed record MeDto(
    string Id,
    string Name,
    string Email,
    long? EmployeeId,
    string? EmployeeCode,
    string? Department,
    string? Designation,
    IReadOnlyList<string> Roles);
