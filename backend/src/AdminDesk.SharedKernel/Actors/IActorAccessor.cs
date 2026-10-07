namespace AdminDesk.SharedKernel.Actors;

public interface IActorAccessor
{
    // The current actor's user id; the system actor id when none was set.
    string UserId { get; }

    // Sets the actor for the current scope.
    void Use(string userId);
}
