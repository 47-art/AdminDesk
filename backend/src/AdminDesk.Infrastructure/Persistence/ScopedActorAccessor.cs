using AdminDesk.SharedKernel.Actors;
using AdminDesk.SharedKernel.Constants;

namespace AdminDesk.Infrastructure.Persistence;

// Holds the acting user id for one dependency-injection scope.
public sealed class ScopedActorAccessor : IActorAccessor
{
    private string _userId = SystemActor.UserId;

    public string UserId => _userId;

    public void Use(string userId)
    {
        _userId = string.IsNullOrWhiteSpace(userId) ? SystemActor.UserId : userId;
    }
}
