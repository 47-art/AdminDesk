namespace AdminDesk.Application.Engine;

// The signed-in person as the engine needs to know them. Roles and employee id come
// from the validated token only.
public sealed record ActorContext(
    string UserId,
    string Name,
    int? EmployeeId,
    IReadOnlySet<string> Roles);
