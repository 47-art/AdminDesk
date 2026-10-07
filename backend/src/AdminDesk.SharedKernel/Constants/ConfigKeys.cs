namespace AdminDesk.SharedKernel.Constants;

public static class ConfigKeys
{
    public const string DemoEnabled = "Demo:Enabled";
    public const string JwtSigningKey = "Jwt:SigningKey";
    public const string JwtIssuer = "Jwt:Issuer";
    public const string JwtAudience = "Jwt:Audience";
    public const string JwtLifetimeHours = "Jwt:LifetimeHours";
    public const string BootstrapAdminEmail = "Bootstrap:AdminEmail";
    public const string BootstrapAdminPassword = "Bootstrap:AdminPassword";
    public const string BootstrapAdminName = "Bootstrap:AdminName";
    public const string CorsAllowedOrigins = "Cors:AllowedOrigins";
    public const string StorageDataDirectory = "Storage:DataDirectory";
    public const string JobsProvider = "Jobs:Provider";
    public const string SpikeEnabled = "Spike:Enabled";
    public const string DefinitionsOverrideDirectory = "Definitions:OverrideDirectory";
    public const string DiagnosticsRunEngineProbe = "Diagnostics:RunEngineProbe";

    // Test-only trigger for the logging checks.
    public const string DiagnosticsEnableLogTest = "Diagnostics:EnableLogTest";

    public const string LoggingDbEnabled = "Logging:Db:Enabled";
    public const string LoggingDbRetentionDaysApp = "Logging:Db:RetentionDays:App";
    public const string LoggingDbRetentionDaysEmail = "Logging:Db:RetentionDays:Email";

    // Diagnostics only: empty means the application database; a different path
    // is used by the logging failure check.
    public const string LoggingDbDatabasePath = "Logging:Db:DatabasePath";

    // Development-only placeholder; the host refuses it when demo mode is off.
    public const string DevelopmentSigningKey = "development-only-signing-key-do-not-use-outside-local-demo-0123456789";

    // Fake shared practice password for the demo accounts.
    public const string DemoPassword = "Demo@12345";
}
