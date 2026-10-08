namespace AdminDesk.Application.Engine;

// The single visibility rule for a request: the requester, any past or current step actor,
// holders of a role that currently has the step, and the Admin and SystemAdmin roles.
// Used by the detail query and the audit trail alike.
public sealed class RequestAccessPolicy
{
    private readonly IRequestRepository _requests;

    public RequestAccessPolicy(IRequestRepository requests)
    {
        _requests = requests;
    }

    public Task<bool> CanViewAsync(ActorContext actor, long requestId, CancellationToken ct) =>
        _requests.IsVisibleToAsync(requestId, actor, ct);
}
