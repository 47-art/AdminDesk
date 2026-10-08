namespace AdminDesk.Application.Engine;

public interface IRequestWorkflowService
{
    // Creates and routes a request; returns its id.
    Task<long> CreateAsync(ActorContext actor, CreateRequestCommand command, CancellationToken ct);

    // Approve, reject, complete or cancel; returns the new row version.
    Task<long> ActAsync(ActorContext actor, long requestId, ActionCommand command, CancellationToken ct);
}
