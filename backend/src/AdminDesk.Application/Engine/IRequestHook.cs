using System.Data.Common;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Application.Engine;

// The open connection and transaction of the unit of work, the actor and the request.
public sealed record HookContext(
    DbConnection Connection,
    DbTransaction Transaction,
    ActorContext Actor,
    RequestSnapshot Request);

public sealed record StepDoneInfo(
    string StepKey,
    RequestAction Action,
    IReadOnlyDictionary<string, object?> Captured);

public sealed record TerminalInfo(RequestStatus FinalStatus);

// Extension seam for reacting to request events. Hooks run inside the same unit of work and
// transaction as the action. No implementation ships with the application.
public interface IRequestHook
{
    // A guard: throw a DomainRuleException to refuse creation.
    Task OnCreatingAsync(HookContext context, CancellationToken ct);

    Task OnStepDoneAsync(HookContext context, StepDoneInfo info, CancellationToken ct);

    // Called for the end states Rejected, Cancelled and Closed.
    Task OnTerminalAsync(HookContext context, TerminalInfo info, CancellationToken ct);
}
