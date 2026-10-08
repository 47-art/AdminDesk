using System.Data.Common;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Application.Engine;

// Every column of a request. Id is 0 until the row is inserted.
public sealed record RequestSnapshot
{
    public long Id { get; init; }
    public string RequestNo { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    public int DefinitionId { get; init; }
    public int DefinitionVersion { get; init; }
    public int RequesterEmployeeId { get; init; }
    public int? DepartmentId { get; init; }
    public int? ProjectId { get; init; }
    public int? LocationId { get; init; }
    public int? CostCentreId { get; init; }
    public DateOnly RequestDate { get; init; }
    public DateOnly? RequiredDate { get; init; }
    public Priority Priority { get; init; }
    public string? Subject { get; init; }
    public ApprovalStatus ApprovalStatus { get; init; }
    public RequestStatus CurrentStatus { get; init; }
    public string? CurrentStepKey { get; init; }
    public int? CurrentStepSeq { get; init; }
    public int? ResponsibleEmployeeId { get; init; }
    public string? ResponsibleRole { get; init; }
    public string? Remarks { get; init; }
    public string PayloadJson { get; init; } = "{}";
    public long? AmountMinor { get; init; }
    public long? ParentRequestId { get; init; }
    public long RowVersion { get; init; }
    public DateTime? ClosedUtc { get; init; }
}

public sealed record RequestStepRow
{
    public long Id { get; init; }
    public long RequestId { get; init; }
    public int Seq { get; init; }
    public string StepKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public StepType StepType { get; init; }
    public StepState State { get; init; }
    public DateTime? ActivatedUtc { get; init; }
    public DateTime? DueUtc { get; init; }
    public string? ActedByUserId { get; init; }
    public string? ActedByName { get; init; }
    public DateTime? ActedUtc { get; init; }

    // The reject reason for a rejected step.
    public string? Comment { get; init; }

    // The values captured at this step as a JSON object; null when nothing was captured.
    public string? CapturedJson { get; init; }
}

public sealed record ActorRow
{
    public long Id { get; init; }
    public long RequestId { get; init; }
    public int StepSeq { get; init; }
    public string? RoleName { get; init; }
    public int? EmployeeId { get; init; }
}

// Reads take a connection, writes take the transaction of the unit of work.
public interface IRequestRepository
{
    // Takes the next number for the module and year; the caller formats the request number.
    Task<long> NextCounterAsync(DbTransaction tx, string moduleCode, int year, CancellationToken ct);

    Task<long> InsertRequestAsync(DbTransaction tx, RequestSnapshot request, CancellationToken ct);

    // Null when the request does not exist or is inactive.
    Task<RequestSnapshot?> GetSnapshotAsync(DbTransaction tx, long id, CancellationToken ct);

    // Compare-and-increment on the row version. Returns the rows affected; zero means the
    // request changed since it was read.
    Task<int> UpdateRequestAsync(DbTransaction tx, RequestSnapshot request, long expectedRowVersion, CancellationToken ct);

    Task InsertStepsAsync(DbTransaction tx, long requestId, IReadOnlyList<RequestStepRow> steps, CancellationToken ct);

    Task UpdateStepAsync(DbTransaction tx, RequestStepRow step, CancellationToken ct);

    Task InsertActorsAsync(DbTransaction tx, long requestId, IReadOnlyList<ActorRow> actors, CancellationToken ct);

    // Marks the request's actor rows inactive; nothing is deleted.
    Task DeactivateActorsAsync(DbTransaction tx, long requestId, CancellationToken ct);

    Task<IReadOnlyList<RequestStepRow>> GetStepsAsync(DbConnection connection, long requestId, CancellationToken ct);

    Task<IReadOnlyList<ActorRow>> GetActiveActorsAsync(DbConnection connection, long requestId, CancellationToken ct);

    // The same reads on the connection of an open transaction, so they see its own writes.
    Task<IReadOnlyList<RequestStepRow>> GetStepsAsync(DbTransaction tx, long requestId, CancellationToken ct);

    Task<IReadOnlyList<ActorRow>> GetActiveActorsAsync(DbTransaction tx, long requestId, CancellationToken ct);

    // True for the requester, any past actor, any current actor (by employee id or role) and
    // the organisation-wide roles (Admin, SystemAdmin and Management). False when the request does not exist.
    Task<bool> IsVisibleToAsync(long requestId, ActorContext actor, CancellationToken ct);
}
