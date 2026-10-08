using AdminDesk.Application.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Jobs;

// Runs one handler inside its own dependency-injection scope. Hangfire needs a method
// call it can serialise, so its scheduler enqueues calls to this generic type.
public sealed class HandlerInvoker<THandler> where THandler : IJobHandler
{
    private readonly IServiceScopeFactory _scopes;

    public HandlerInvoker(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public Task RunAsync(string? argsJson, CancellationToken ct) =>
        RunAsync(Guid.NewGuid().ToString("N"), argsJson, ct);

    public async Task RunAsync(string jobId, string? argsJson, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var correlation = Guid.NewGuid().ToString("N");
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        await handler.RunAsync(new JobContext(jobId, argsJson, correlation), ct);
    }
}
