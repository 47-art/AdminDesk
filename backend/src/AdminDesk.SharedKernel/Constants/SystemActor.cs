namespace AdminDesk.SharedKernel.Constants;

// Stamped into created_by and updated_by for seed and startup writes.
// The value can never equal an Identity user id, which is a GUID.
public static class SystemActor
{
    public const string UserId = "system";
    public const string Name = "System";
}
