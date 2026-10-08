using AdminDesk.Domain.Definitions;

namespace AdminDesk.Domain.Engine;

// Names who may act on a step. A requester who holds a step's role may act on task steps, but
// not approve or reject their own request (see TransitionRules.BarredAsRequester). A reporting manager step with no manager yields no slot and
// the step waits.
public static class StepResolver
{
    public static IReadOnlyList<ActorSlot> ResolveActors(StepDefinition step, RequesterInfo requester)
    {
        var slots = new List<ActorSlot>();
        var actor = step.Actor;
        if (actor is null)
        {
            return slots;
        }
        if (actor.ReportingManager && requester.ManagerEmployeeId is { } managerId)
        {
            slots.Add(new ActorSlot(null, managerId));
        }
        if (actor.Requester)
        {
            slots.Add(new ActorSlot(null, requester.EmployeeId));
        }
        if (actor.Roles is not null)
        {
            foreach (var role in actor.Roles)
            {
                slots.Add(new ActorSlot(role, null));
            }
        }
        return slots;
    }
}
