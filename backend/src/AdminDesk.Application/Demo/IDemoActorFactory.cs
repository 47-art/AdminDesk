using AdminDesk.Application.Engine;

namespace AdminDesk.Application.Demo;

// Builds the acting person for sample data that is created through the workflow engine.
public interface IDemoActorFactory
{
    // The actor for one of the fixed demo accounts, identified by its role.
    Task<ActorContext> ForAccountAsync(string role, CancellationToken ct);

    // A plain employee actor for a generated employee who has no login.
    Task<ActorContext> ForEmployeeAsync(string employeeCode, CancellationToken ct);
}
