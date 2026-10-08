namespace AdminDesk.Application.Jobs;

// What a handler receives when a job runs. ArgsJson is the JSON text given when the
// job was scheduled, or null when it had no arguments.
public sealed record JobContext(string JobId, string? ArgsJson, string? CorrelationId);

// A unit of background work. Handlers are resolved from a dependency-injection scope
// created for each run, so they may take scoped services.
public interface IJobHandler
{
    Task RunAsync(JobContext context, CancellationToken ct);
}
