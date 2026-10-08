using System.Text;
using AdminDesk.SharedKernel.Constants;

namespace AdminDesk.Api.Auth;

public sealed class JwtSettings
{
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "admindesk";
    public string Audience { get; set; } = "admindesk-web";
    public int LifetimeHours { get; set; } = 8;
    public string SigningKey { get; set; } = string.Empty;

    public static JwtSettings Load(IConfiguration configuration)
    {
        var settings = new JwtSettings
        {
            Issuer = configuration[ConfigKeys.JwtIssuer] ?? "admindesk",
            Audience = configuration[ConfigKeys.JwtAudience] ?? "admindesk-web",
            SigningKey = configuration[ConfigKeys.JwtSigningKey] ?? string.Empty,
        };
        if (int.TryParse(configuration[ConfigKeys.JwtLifetimeHours], out var hours) && hours > 0)
        {
            settings.LifetimeHours = hours;
        }

        var demo = bool.TryParse(configuration[ConfigKeys.DemoEnabled], out var enabled) && enabled;
        settings.Validate(demo);
        return settings;
    }

    // The messages name the setting only; the key value is never written anywhere.
    public void Validate(bool demoEnabled)
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                $"{ConfigKeys.JwtSigningKey} must be at least {MinimumKeyBytes} bytes long.");
        }
        if (!demoEnabled && string.Equals(SigningKey, ConfigKeys.DevelopmentSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{ConfigKeys.JwtSigningKey} still has the development placeholder value; set a private key when demo mode is off.");
        }
    }
}
