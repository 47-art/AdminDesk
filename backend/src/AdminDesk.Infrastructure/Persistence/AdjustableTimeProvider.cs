namespace AdminDesk.Infrastructure.Persistence;

// Behaves as the system clock unless an override instant is set. The override is a
// fixed instant that does not advance.
public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset? _override;

    public bool IsOverridden => _override.HasValue;

    public void SetOverride(DateTimeOffset? instant) => _override = instant;

    public override DateTimeOffset GetUtcNow() => _override ?? base.GetUtcNow();
}
