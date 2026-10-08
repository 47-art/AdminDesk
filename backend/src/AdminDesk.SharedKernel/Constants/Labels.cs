namespace AdminDesk.SharedKernel.Constants;

// Wording the engine and the query side share, so the same text is not typed in two places.
public static class Labels
{
    public const string Requester = "Requester";
    public const string ReportingManager = "Reporting manager";
    public const string Approve = "Approve";
    public const string Complete = "Complete";
}

// Sort directions accepted by the list endpoints.
public static class SortDirections
{
    public const string Ascending = "asc";
    public const string Descending = "desc";
}

// Names of the diagnostic log test modes.
public static class LogTestModes
{
    public const string Error = "error";
    public const string Warning = "warning";
}
