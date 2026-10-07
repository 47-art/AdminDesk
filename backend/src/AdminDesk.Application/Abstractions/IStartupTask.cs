namespace AdminDesk.Application.Abstractions;

// Work that must finish before the API accepts requests. Each task runs in its own
// dependency-injection scope, in Order; a failure stops startup.
public interface IStartupTask
{
    int Order { get; }

    Task RunAsync(CancellationToken ct);
}
