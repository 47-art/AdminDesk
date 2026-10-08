using System.Text.Json;
using AdminDesk.Application.Engine;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Application.Requests;

// What a client may send when raising a request. Requester, status, step and responsible
// person are never accepted: the server derives them.
public sealed class CreateRequestBody
{
    public string ModuleCode { get; set; } = string.Empty;

    public int DefinitionId { get; set; }

    public CommonFieldsBody Common { get; set; } = new();

    public Dictionary<string, JsonElement> Payload { get; set; } = new();

    public CreateRequestCommand ToCommand() => new()
    {
        ModuleCode = ModuleCode,
        DefinitionId = DefinitionId,
        Common = (Common ?? new CommonFieldsBody()).ToCommon(),
        Payload = Payload ?? new Dictionary<string, JsonElement>()
    };
}

public sealed class CommonFieldsBody
{
    public int? ProjectId { get; set; }

    public int? LocationId { get; set; }

    public int? CostCentreId { get; set; }

    public DateOnly? RequiredDate { get; set; }

    // Read as text so an unknown value is reported as a field error rather than a binding failure.
    public string? Priority { get; set; }

    public string? Remarks { get; set; }

    public CommonFields ToCommon() => new()
    {
        ProjectId = ProjectId,
        LocationId = LocationId,
        CostCentreId = CostCentreId,
        RequiredDate = RequiredDate,
        Priority = RequestParsing.ParsePriority(Priority),
        Remarks = Remarks
    };
}

public sealed class ActionBody
{
    public string Action { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public long RowVersion { get; set; }

    public Dictionary<string, JsonElement>? Captured { get; set; }

    public ActionCommand ToCommand() => new()
    {
        Action = RequestParsing.ParseEnum<RequestAction>(Action) ?? RequestAction.Approve,
        Comment = Comment,
        Captured = Captured,
        ExpectedRowVersion = RowVersion
    };
}

// Query string of GET api/requests/mine. Status may repeat.
public sealed class MineQuery
{
    public List<string> Status { get; set; } = new();

    public string? ApprovalStatus { get; set; }

    public string? Module { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public string? Q { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? Sort { get; set; }

    public string? Dir { get; set; }
}

// Query string of GET api/requests/inbox.
public sealed class InboxQuery
{
    public string? Module { get; set; }

    public string? Requester { get; set; }

    public string? Priority { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public static class RequestParsing
{
    public const int MaxPageSize = 100;

    // Whole names only: numbers and unknown words are not accepted.
    public static T? ParseEnum<T>(string? text) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is null ? null : Enum.Parse<T>(name);
    }

    // An unknown text becomes a value that is not defined, which the engine reports as a priority error.
    public static Priority ParsePriority(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Priority.Medium : ParseEnum<Priority>(text) ?? (Priority)(-1);

    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date : null;
}
