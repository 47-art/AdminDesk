using System.Globalization;
using AdminDesk.SharedKernel.Actors;
using AdminDesk.SharedKernel.Constants;

namespace AdminDesk.Infrastructure.Persistence;

// Property names match the Dapper parameters used by AuditSql.
public sealed record AuditStamp(string CreatedUtc, string CreatedBy, string UpdatedUtc, string UpdatedBy);

// The one place that produces the created and updated stamp values for business rows.
public sealed class AuditStamper
{
    private readonly IActorAccessor _actor;
    private readonly TimeProvider _time;

    public AuditStamper(IActorAccessor actor, TimeProvider time)
    {
        _actor = actor;
        _time = time;
    }

    public AuditStamp ForCreate() => Build(_actor.UserId);

    public AuditStamp ForUpdate() => Build(_actor.UserId);

    // Seeds and startup work always run as the system actor.
    public AuditStamp ForSystem() => Build(SystemActor.UserId);

    private AuditStamp Build(string actor)
    {
        var now = _time.GetUtcNow().UtcDateTime.ToString(DapperTypeHandlers.DateTimeFormat, CultureInfo.InvariantCulture);
        return new AuditStamp(now, actor, now, actor);
    }
}
