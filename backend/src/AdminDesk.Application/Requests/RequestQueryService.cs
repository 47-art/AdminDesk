using System.Globalization;
using System.Text.Json;
using AdminDesk.Application.Definitions;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.Domain.Definitions;
using AdminDesk.Domain.Engine;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Money;
using AdminDesk.SharedKernel.Time;

namespace AdminDesk.Application.Requests;

public interface IRequestQueryService
{
    Task<RequestDetailDto> GetDetailAsync(ActorContext actor, long id, CancellationToken ct);

    Task<IReadOnlyList<AuditEventDto>> GetAuditAsync(ActorContext actor, long id, CancellationToken ct);

    Task<PagedResult<RequestListItem>> MineAsync(ActorContext actor, MineQuery query, CancellationToken ct);

    Task<PagedResult<RequestListItem>> AllAsync(ActorContext actor, MineQuery query, CancellationToken ct);

    Task<PagedResult<RequestListItem>> TeamAsync(ActorContext actor, MineQuery query, CancellationToken ct);

    Task<PagedResult<RequestListItem>> InboxAsync(ActorContext actor, InboxQuery query, CancellationToken ct);

    Task<int> InboxCountAsync(ActorContext actor, CancellationToken ct);

    Task<DashboardSummary> SummaryAsync(ActorContext actor, CancellationToken ct);
}

public sealed class RequestQueryService : IRequestQueryService
{
    private const int RecentCount = 5;
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private readonly IRequestQueryRepository _repository;
    private readonly IDefinitionProvider _definitions;
    private readonly RequestAccessPolicy _access;
    private readonly ILookupRegistry _lookups;
    private readonly TimeProvider _clock;

    public RequestQueryService(
        IRequestQueryRepository repository,
        IDefinitionProvider definitions,
        RequestAccessPolicy access,
        ILookupRegistry lookups,
        TimeProvider clock)
    {
        _repository = repository;
        _definitions = definitions;
        _access = access;
        _lookups = lookups;
        _clock = clock;
    }

    // ---------------------------------------------------------------- detail

    public async Task<RequestDetailDto> GetDetailAsync(ActorContext actor, long id, CancellationToken ct)
    {
        // A request the caller may not see looks exactly like one that does not exist.
        if (!await _access.CanViewAsync(actor, id, ct))
        {
            throw NotFound();
        }
        var rows = await _repository.GetDetailAsync(id, ct) ?? throw NotFound();
        var header = rows.Header;

        var issued = await _definitions.GetByIdAsync(header.DefinitionId, ct)
            ?? throw new InvalidOperationException($"Definition {header.DefinitionId} of request {header.Id} is missing.");
        var definition = issued.Definition;

        var status = Enum.Parse<RequestStatus>(header.CurrentStatus);
        var currentSeq = header.CurrentStepSeq is { } s ? (int?)checked((int)s) : null;

        var labels = new Dictionary<string, string>();
        AddIfPresent(labels, "projectId", header.ProjectName);
        AddIfPresent(labels, "locationId", header.LocationName);
        AddIfPresent(labels, "costCentreId", header.CostCentreName);
        await AddLookupLabelsAsync(labels, definition.Fields, header.PayloadJson, string.Empty, ct);

        var steps = new List<RequestStepDto>();
        foreach (var row in rows.Steps.OrderBy(r => r.Seq))
        {
            var stepDefinition = definition.Steps.FirstOrDefault(d => d.Key == row.StepKey);
            var captureFields = stepDefinition?.CaptureFields ?? Array.Empty<FieldDefinition>();
            IReadOnlyDictionary<string, object?>? captured = null;
            if (!string.IsNullOrWhiteSpace(row.CapturedJson))
            {
                captured = ToWire(captureFields, row.CapturedJson);
                await AddLookupLabelsAsync(labels, captureFields, row.CapturedJson, row.StepKey + ".", ct);
            }
            steps.Add(new RequestStepDto(
                row.Seq,
                row.StepKey,
                row.Name,
                row.StepType,
                row.State,
                stepDefinition is null ? string.Empty : ModuleCatalogService.ActorLabelFor(stepDefinition),
                row.ActedByName,
                row.ActedUtc,
                row.Comment,
                currentSeq == row.Seq && row.State != StepState.Upcoming,
                captured,
                captureFields.Select(ModuleCatalogService.FieldDtoFor).ToList(),
                stepDefinition?.RequiresDocument ?? false));
        }

        var current = currentSeq is { } seq ? steps.FirstOrDefault(x => x.Seq == seq) : null;
        var currentDefinition = current is null ? null : definition.Steps.FirstOrDefault(d => d.Key == current.Key);
        StepType? currentType = current?.Type;

        var allowed = AllowedActionsCalculator.Compute(
            actor, status, header.RequesterEmployeeId, currentSeq, currentType, rows.ActiveActors,
            TransitionRules.CancelLocked(definition.Steps, rows.Steps.Select(r => (r.StepKey, r.State))),
            rows.Steps.Any(r => r.Seq == currentSeq && r.State == StepState.Pending));
        var primary = AllowedActionsCalculator.PrimaryLabel(status, currentType, currentDefinition?.ActionLabel);

        string? cancelReason = null;
        string? cancelledUtc = null;
        string? stoppedByName = null;
        string? stoppedByRole = null;
        if (status == RequestStatus.Cancelled && rows.Stopped is { EventType: AuditEventTypes.Cancelled } cancelled)
        {
            cancelReason = cancelled.Comment;
            cancelledUtc = cancelled.CreatedUtc.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture);
            stoppedByName = cancelled.ActorName;
            stoppedByRole = cancelled.ActorRole;
        }
        else if (status == RequestStatus.Rejected && rows.Stopped is { EventType: AuditEventTypes.Rejected } rejected)
        {
            stoppedByName = rejected.ActorName;
            stoppedByRole = rejected.ActorRole;
        }

        return new RequestDetailDto(
            header.Id,
            header.RequestNo,
            header.ModuleCode,
            definition.Name,
            header.DefinitionId,
            header.Subject,
            header.RowVersion,
            new RequesterDto(header.RequesterEmployeeId, header.RequesterCode, header.RequesterName, header.DepartmentName),
            header.DepartmentName,
            Label(header.ProjectId, header.ProjectName),
            Label(header.LocationId, header.LocationName),
            Label(header.CostCentreId, header.CostCentreName),
            header.RequestDate,
            header.RequiredDate,
            Enum.Parse<Priority>(header.Priority),
            Enum.Parse<ApprovalStatus>(header.ApprovalStatus),
            status,
            header.CurrentStepKey,
            current?.Name,
            new ResponsibleDto(header.ResponsibleName, header.ResponsibleRole),
            header.Remarks,
            ToWire(definition.Fields, header.PayloadJson),
            labels,
            AgeDays(header.RequestDate, header.ClosedUtc),
            header.CreatedUtc,
            header.ClosedUtc,
            ModuleCatalogService.ToDto(issued),
            steps,
            allowed,
            primary,
            cancelReason,
            cancelledUtc,
            stoppedByName,
            stoppedByRole);
    }

    // ----------------------------------------------------------------- audit

    public async Task<IReadOnlyList<AuditEventDto>> GetAuditAsync(ActorContext actor, long id, CancellationToken ct)
    {
        if (!actor.Roles.Overlaps(Roles.AuditViewers))
        {
            throw new ForbiddenException("You cannot read the audit trail.");
        }
        if (!await _repository.ExistsAsync(id, ct))
        {
            throw NotFound();
        }
        var rows = await _repository.ListAuditAsync(id, ct);
        return rows
            .Select(r => new AuditEventDto(
                r.Id, r.EventType, r.ActorName, r.ActorRole, r.StepKey, r.StepName, r.FromStatus, r.ToStatus, r.Comment, r.CreatedUtc))
            .ToList();
    }

    // ----------------------------------------------------------------- lists

    public async Task<PagedResult<RequestListItem>> MineAsync(ActorContext actor, MineQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, RequestParsing.MaxPageSize);
        if (actor.EmployeeId is not { } employeeId)
        {
            return new PagedResult<RequestListItem>(Array.Empty<RequestListItem>(), 0, page, pageSize);
        }

        var filter = ToFilter(query, page, pageSize);
        var rows = await _repository.ListMineAsync(employeeId, filter, ct);
        var items = await ToItemsAsync(rows.Items, ct);
        return new PagedResult<RequestListItem>(items, rows.Total, page, pageSize);
    }

    public async Task<PagedResult<RequestListItem>> AllAsync(ActorContext actor, MineQuery query, CancellationToken ct)
    {
        // The route policy is the first guard; this keeps the service safe when called from elsewhere.
        if (!actor.Roles.Any(Roles.OrganisationWide.Contains))
        {
            throw new ForbiddenException("You cannot see every request.");
        }
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, RequestParsing.MaxPageSize);
        var rows = await _repository.ListAllAsync(ToFilter(query, page, pageSize), ct);
        var items = await ToItemsAsync(rows.Items, ct);
        return new PagedResult<RequestListItem>(items, rows.Total, page, pageSize);
    }

    public async Task<PagedResult<RequestListItem>> TeamAsync(ActorContext actor, MineQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, RequestParsing.MaxPageSize);
        if (actor.EmployeeId is not { } managerId)
        {
            return new PagedResult<RequestListItem>(Array.Empty<RequestListItem>(), 0, page, pageSize);
        }
        var rows = await _repository.ListTeamAsync(managerId, ToFilter(query, page, pageSize), ct);
        var items = await ToItemsAsync(rows.Items, ct);
        return new PagedResult<RequestListItem>(items, rows.Total, page, pageSize);
    }

    private static MineFilter ToFilter(MineQuery query, int page, int pageSize) =>
        new(
            string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim(),
            query.Status.Select(s => RequestParsing.ParseEnum<RequestStatus>(s)).Where(s => s is not null).Select(s => s!.Value).Distinct().ToList(),
            RequestParsing.ParseEnum<ApprovalStatus>(query.ApprovalStatus),
            string.IsNullOrWhiteSpace(query.Module) ? null : query.Module.Trim(),
            RequestParsing.ParseDate(query.From),
            RequestParsing.ParseDate(query.To),
            page,
            pageSize,
            query.Sort,
            query.Dir);

    public async Task<PagedResult<RequestListItem>> InboxAsync(ActorContext actor, InboxQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, RequestParsing.MaxPageSize);
        var filter = new InboxFilter(
            string.IsNullOrWhiteSpace(query.Module) ? null : query.Module.Trim(),
            string.IsNullOrWhiteSpace(query.Requester) ? null : query.Requester.Trim(),
            RequestParsing.ParseEnum<Priority>(query.Priority),
            page,
            pageSize);

        var rows = await _repository.ListInboxAsync(actor.EmployeeId, actor.Roles, filter, ct);
        var items = await ToItemsAsync(rows.Items, ct);
        return new PagedResult<RequestListItem>(items, rows.Total, page, pageSize);
    }

    public Task<int> InboxCountAsync(ActorContext actor, CancellationToken ct) =>
        _repository.CountInboxAsync(actor.EmployeeId, actor.Roles, ct);

    public async Task<DashboardSummary> SummaryAsync(ActorContext actor, CancellationToken ct)
    {
        var waiting = await _repository.CountInboxAsync(actor.EmployeeId, actor.Roles, ct);
        var orgWide = actor.Roles.Any(Roles.OrganisationWide.Contains);
        var scope = orgWide ? DashboardScopes.Organisation : DashboardScopes.Mine;
        if (actor.EmployeeId is not { } employeeId)
        {
            if (!orgWide)
            {
                return new DashboardSummary(waiting, 0, 0, 0, 0, 0, 0, scope, Array.Empty<RequestListItem>());
            }

            var all = await _repository.SummaryAsync(null, ct);
            return ToSummary(waiting, all, scope, Array.Empty<RequestListItem>());
        }

        var counts = await _repository.SummaryAsync(orgWide ? null : employeeId, ct);
        var recent = await _repository.ListRecentAsync(employeeId, actor.UserId, RecentCount, ct);
        return ToSummary(waiting, counts, scope, await ToItemsAsync(recent, ct));
    }

    private static DashboardSummary ToSummary(int waiting, SummaryCounts counts, string scope, IReadOnlyList<RequestListItem> recent) =>
        new(waiting, (int)counts.Total, (int)counts.Pending, (int)counts.Approved, (int)counts.Rejected,
            (int)counts.Completed, (int)counts.Cancelled, scope, recent);

    // Each distinct pinned definition is loaded once for the whole page.
    private async Task<IReadOnlyList<RequestListItem>> ToItemsAsync(IReadOnlyList<RequestListRow> rows, CancellationToken ct)
    {
        var definitions = new Dictionary<long, ModuleDefinition>();
        foreach (var id in rows.Select(r => r.DefinitionId).Distinct())
        {
            var issued = await _definitions.GetByIdAsync(id, ct);
            if (issued is not null)
            {
                definitions[id] = issued.Definition;
            }
        }

        var items = new List<RequestListItem>(rows.Count);
        foreach (var row in rows)
        {
            definitions.TryGetValue(row.DefinitionId, out var definition);
            var status = Enum.Parse<RequestStatus>(row.CurrentStatus);
            var inProgress = status == RequestStatus.InProgress;
            var step = inProgress && row.CurrentStepKey is not null
                ? definition?.Steps.FirstOrDefault(d => d.Key == row.CurrentStepKey)
                : null;

            items.Add(new RequestListItem(
                row.Id,
                row.RequestNo,
                row.ModuleCode,
                definition?.Name ?? row.ModuleCode,
                row.Subject,
                row.RequestDate,
                row.RequiredDate,
                Enum.Parse<Priority>(row.Priority),
                status,
                inProgress ? row.CurrentStepName : null,
                step?.Type,
                row.ResponsibleName,
                row.ResponsibleRole,
                row.UpdatedUtc,
                row.RequesterName,
                row.RequesterDepartment,
                AgeDays(row.RequestDate, row.ClosedUtc),
                AllowedActionsCalculator.PrimaryLabel(status, step?.Type, step?.ActionLabel),
                (step?.CaptureFields ?? Array.Empty<FieldDefinition>()).Select(ModuleCatalogService.FieldDtoFor).ToList(),
                step?.RequiresDocument ?? false));
        }
        return items;
    }

    // ---------------------------------------------------------------- helpers

    private static NotFoundException NotFound() => new("Request not found.");

    private static LabelDto? Label(long? id, string? label) =>
        id is { } value && label is not null ? new LabelDto(value, label) : null;

    private static void AddIfPresent(Dictionary<string, string> labels, string key, string? label)
    {
        if (label is not null)
        {
            labels[key] = label;
        }
    }

    private int AgeDays(DateOnly requestDate, DateTime? closedUtc)
    {
        var end = closedUtc is { } closed ? DateOnly.FromDateTime(closed.ToUniversalTime()) : IndiaTime.Today(_clock);
        return Math.Max(0, end.DayNumber - requestDate.DayNumber);
    }

    // Looks up the label of every lookup value stored in the JSON, under prefix + field key.
    private async Task AddLookupLabelsAsync(
        Dictionary<string, string> labels, IReadOnlyList<FieldDefinition> fields, string json, string prefix, CancellationToken ct)
    {
        var lookupFields = fields.Where(f => f.Type == FieldType.Lookup && f.LookupKind is not null).ToList();
        if (lookupFields.Count == 0)
        {
            return;
        }
        using var document = JsonDocument.Parse(json);
        var wanted = new List<(string Key, string Kind, long Id)>();
        foreach (var field in lookupFields)
        {
            if (document.RootElement.TryGetProperty(field.Key, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                wanted.Add((field.Key, field.LookupKind!, value.GetInt64()));
            }
        }

        // One query per lookup kind rather than one per field.
        foreach (var group in wanted.GroupBy(w => w.Kind))
        {
            var provider = _lookups.Find(group.Key);
            if (provider is null)
            {
                continue;
            }
            var items = (await provider.GetManyAsync(group.Select(w => w.Id).ToList(), ct)).ToDictionary(i => i.Id);
            foreach (var (key, _, id) in group)
            {
                if (items.TryGetValue(id, out var item))
                {
                    labels[prefix + key] = item.Label;
                }
            }
        }
    }

    // Stored values to wire values: money goes from integer paise back to rupees, the rest is unchanged.
    private static Dictionary<string, object?> ToWire(IReadOnlyList<FieldDefinition> fields, string json)
    {
        var result = new Dictionary<string, object?>();
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var field = fields.FirstOrDefault(f => f.Key == property.Name);
            if (field?.Type == FieldType.Money && property.Value.ValueKind == JsonValueKind.Number)
            {
                // Dividing by a value with many decimals drops trailing zeros: 1250.50 reads as 1250.5.
                result[property.Name] = MoneyConverter.ToRupees(property.Value.GetInt64()) / 1.0000000000000000000000000000m;
            }
            else
            {
                result[property.Name] = property.Value.Clone();
            }
        }
        return result;
    }
}
