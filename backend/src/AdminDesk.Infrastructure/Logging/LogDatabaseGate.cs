namespace AdminDesk.Infrastructure.Logging;

// Marks the moment the logs table is known to exist. The database sink holds its
// events until then, so it never touches a half-migrated database.
public static class LogDatabaseGate
{
    private static readonly TaskCompletionSource Opened =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static bool IsOpen => Opened.Task.IsCompleted;

    // Idempotent.
    public static void Open() => Opened.TrySetResult();

    public static Task WhenOpenAsync(CancellationToken ct) => Opened.Task.WaitAsync(ct);
}
