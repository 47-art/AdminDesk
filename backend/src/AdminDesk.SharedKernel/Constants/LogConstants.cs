namespace AdminDesk.SharedKernel.Constants;

public static class LogConstants
{
    public const string Table = "logs";

    public const string TimestampUtc = "timestamp_utc";
    public const string Level = "level";
    public const string Category = "category";
    public const string Message = "message";
    public const string MessageTemplate = "message_template";
    public const string Exception = "exception";
    public const string CorrelationId = "correlation_id";
    public const string PropertiesJson = "properties_json";

    public const string CategoryApp = "App";
    // Reserved for email events; nothing writes it yet.
    public const string CategoryEmail = "Email";

    public const string PropRecipient = "recipient";
    public const string PropSubject = "subject";
    public const string PropTemplate = "template";
    public const string PropStatus = "status";
    public const string PropError = "error";

    public const string StatusSent = "Sent";
    public const string StatusFailed = "Failed";
    public const string StatusSkipped = "Skipped";

    public const string CorrelationIdProperty = "CorrelationId";
    // An event may carry this property to choose its category (default App).
    public const string CategoryProperty = "LogCategory";
    // An event carrying this property is never persisted.
    public const string SkipDbSinkProperty = "SkipDbSink";

    public const int BatchSize = 100;
    public const int FlushIntervalSeconds = 2;
    public const int QueueCapacity = 10000;
    public const int ShutdownFlushSeconds = 5;
    public const string MinimumPersistedLevel = "Warning";

    public const int DefaultRetentionDaysApp = 30;
    public const int DefaultRetentionDaysEmail = 365;
}
