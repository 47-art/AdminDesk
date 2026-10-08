using System.Text.Json;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Application.Engine;

public sealed record CommonFields
{
    public int? ProjectId { get; init; }
    public int? LocationId { get; init; }
    public int? CostCentreId { get; init; }
    public DateOnly? RequiredDate { get; init; }
    public Priority Priority { get; init; } = Priority.Medium;
    public string? Remarks { get; init; }
}

// A request is created and routed in one call.
public sealed record CreateRequestCommand
{
    public string ModuleCode { get; init; } = string.Empty;
    public int DefinitionId { get; init; }
    public CommonFields Common { get; init; } = new();
    public Dictionary<string, JsonElement> Payload { get; init; } = new();
}

// Comment is read only for Reject and Cancel, where it must be non-blank after trimming and
// at most 1000 characters; it is ignored for Approve and Complete. Captured is keyed by capture
// field key and read only for Complete.
public sealed record ActionCommand
{
    public RequestAction Action { get; init; }
    public string? Comment { get; init; }
    public Dictionary<string, JsonElement>? Captured { get; init; }
    public long ExpectedRowVersion { get; init; }
}
