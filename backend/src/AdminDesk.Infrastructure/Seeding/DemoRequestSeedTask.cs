using System.Text.Json;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Definitions;
using AdminDesk.Application.Demo;
using AdminDesk.Application.Engine;
using AdminDesk.Application.Masters;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Seeding;

// Creates a spread of sample requests on a database that has none, only in demo mode. Every
// request is created and moved along through the workflow service, with the clock set to an
// earlier instant for each step, so the audit trail and the timestamps are real.
public sealed class DemoRequestSeedTask : IStartupTask
{
    private static readonly TimeSpan StepGap = TimeSpan.FromHours(3);

    private readonly IConfiguration _configuration;
    private readonly IRequestWorkflowService _service;
    private readonly IDefinitionProvider _definitions;
    private readonly IDemoActorFactory _actors;
    private readonly IEmployeeRepository _employees;
    private readonly IDbConnectionFactory _factory;
    private readonly AdjustableTimeProvider _clock;
    private readonly ILogger<DemoRequestSeedTask> _logger;

    public DemoRequestSeedTask(
        IConfiguration configuration,
        IRequestWorkflowService service,
        IDefinitionProvider definitions,
        IDemoActorFactory actors,
        IEmployeeRepository employees,
        IDbConnectionFactory factory,
        AdjustableTimeProvider clock,
        ILogger<DemoRequestSeedTask> logger)
    {
        _configuration = configuration;
        _service = service;
        _definitions = definitions;
        _actors = actors;
        _employees = employees;
        _factory = factory;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 60;

    private sealed record Move(ActorContext Actor, RequestAction Action, string? Comment = null, Dictionary<string, JsonElement>? Captured = null);

    public async Task RunAsync(CancellationToken ct)
    {
        if (!bool.TryParse(_configuration[ConfigKeys.DemoEnabled], out var enabled) || !enabled)
        {
            return;
        }

        await using (var connection = await _factory.OpenAsync(ct))
        {
            var demoUsers = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition("SELECT COUNT(*) FROM AspNetUsers", cancellationToken: ct));
            var existing = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition("SELECT COUNT(*) FROM requests", cancellationToken: ct));
            if (demoUsers == 0 || existing > 0)
            {
                _logger.LogInformation("Demo requests not created (users: {Users}, requests: {Requests})", demoUsers, existing);
                return;
            }
        }

        var priya = await _actors.ForAccountAsync(Roles.Employee, ct);
        var manager = await _actors.ForAccountAsync(Roles.Manager, ct);
        var admin = await _actors.ForAccountAsync(Roles.Admin, ct);
        var store = await _actors.ForAccountAsync(Roles.Store, ct);

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset Ago(int days, int hours = 0) => now.AddDays(-days).AddHours(-hours);

        try
        {
            // Stationery requests raised by the Employee, left at different points of the flow.
            var stationeryFlow = new[]
            {
                new Move(manager, RequestAction.Approve),
                new Move(store, RequestAction.Approve),
                new Move(store, RequestAction.Complete),
                new Move(store, RequestAction.Complete),
                new Move(priya, RequestAction.Complete),
                new Move(store, RequestAction.Complete)
            };

            await StationeryAsync(priya, "A4 paper", 5, Ago(1, 2), Array.Empty<Move>(), ct);
            await StationeryAsync(priya, "Ballpoint pens", 20, Ago(3), stationeryFlow.Take(1), ct);
            await StationeryAsync(priya, "Stapler pins", 10, Ago(5), stationeryFlow.Take(2), ct);
            await StationeryAsync(priya, "File folders", 15, Ago(7), stationeryFlow.Take(3), ct);
            await StationeryAsync(priya, "Printer toner", 2, Ago(9), stationeryFlow.Take(4), ct);
            await StationeryAsync(priya, "Whiteboard markers", 12, Ago(11), stationeryFlow.Take(5), ct);
            await StationeryAsync(priya, "Notebooks", 8, Ago(14), stationeryFlow, ct);
            await StationeryAsync(priya, "Highlighters", 30, Ago(16),
                new[] { new Move(manager, RequestAction.Reject, "Please consolidate with the existing stock first") }, ct);
            await StationeryAsync(priya, "Desk organiser", 1, Ago(18),
                new[] { new Move(priya, RequestAction.Cancel, "Raised by mistake, the items are no longer needed") }, ct);

            // Courier requests. The last step needs an uploaded document, so none of them is closed here.
            await CourierAsync(priya, "Rao Associates", "Pune", Ago(2), Array.Empty<Move>(), ct);
            await CourierAsync(priya, "Mehta Traders", "Mumbai", Ago(6), new[]
            {
                new Move(admin, RequestAction.Complete, null, Captured("courierCompany", "Example Couriers")),
                new Move(admin, RequestAction.Complete)
            }, ct);
            await CourierAsync(priya, "Shah Industries", "Ahmedabad", Ago(21), new[]
            {
                new Move(admin, RequestAction.Complete, null, Captured("courierCompany", "Example Couriers")),
                new Move(admin, RequestAction.Complete),
                new Move(admin, RequestAction.Complete, null, Captured("trackingNumber", "TRK482915")),
                new Move(priya, RequestAction.Complete)
            }, ct);

            await ServiceModulesAsync(priya, manager, admin, Ago, ct);

            // Requests from generated employees, waiting for the Manager.
            var managerEmployee = await _employees.GetByCodeAsync("E0009", ct);
            if (managerEmployee is not null)
            {
                var reports = await _employees.ListDirectReportsAsync(managerEmployee.Id, null, 1, 50, ct);
                var people = reports.Items.Where(r => r.Code != "E0010").OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
                var items = new[] { "Sticky notes", "Envelopes", "Marker pens", "Paper clips", "Staplers" };
                for (var i = 0; i < 5 && people.Count > 0; i++)
                {
                    var person = await _actors.ForEmployeeAsync(people[i % people.Count].Code, ct);
                    await StationeryAsync(person, items[i], 2 + i, Ago(1 + i, 5), Array.Empty<Move>(), ct);
                }
            }
        }
        finally
        {
            _clock.SetOverride(null);
        }

        await using var summary = await _factory.OpenAsync(ct);
        var counts = await summary.QueryAsync<(string Status, long Total)>(
            new CommandDefinition("SELECT current_status AS Status, COUNT(*) AS Total FROM requests GROUP BY current_status", cancellationToken: ct));
        foreach (var (status, total) in counts)
        {
            _logger.LogInformation("Demo requests created with status {Status}: {Count}", status, total);
        }
    }

    // One request each for the allocation and service modules, left at a visible stage.
    private async Task ServiceModulesAsync(ActorContext priya, ActorContext manager, ActorContext admin,
        Func<int, int, DateTimeOffset> ago, CancellationToken ct)
    {
        long? sim;
        long? location;
        (long Id, long HolderId)? heldAsset;
        await using (var connection = await _factory.OpenAsync(ct))
        {
            sim = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT id FROM sims WHERE status = 'Available' AND is_active = 1 AND deleted_utc IS NULL ORDER BY id LIMIT 1", cancellationToken: ct));
            location = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT id FROM locations WHERE is_active = 1 ORDER BY id LIMIT 1", cancellationToken: ct));
            var held = await connection.QueryAsync<(long Id, long HolderId)>(new CommandDefinition(
                "SELECT id AS Id, holder_employee_id AS HolderId FROM assets WHERE status = 'Allocated' AND holder_employee_id IS NOT NULL " +
                "AND is_active = 1 AND deleted_utc IS NULL ORDER BY id LIMIT 1", cancellationToken: ct));
            heldAsset = held.Select(h => ((long, long)?)h).FirstOrDefault();
        }

        // SIM waiting for the Admin verification, and one taken all the way to the master update.
        await RunAsync("sim", priya, SimPayload("New SIM", "Field visits need a company number"), ago(2, 3),
            new[] { new Move(manager, RequestAction.Approve) }, ct);
        if (sim is not null)
        {
            await RunAsync("sim", priya, SimPayload("Replacement", "Old SIM stopped working"), ago(12, 0), new[]
            {
                new Move(manager, RequestAction.Approve),
                new Move(admin, RequestAction.Approve),
                new Move(admin, RequestAction.Complete),
                new Move(admin, RequestAction.Complete, null, new() { ["sim"] = JsonSerializer.SerializeToElement(sim.Value) }),
                new Move(priya, RequestAction.Complete),
                new Move(admin, RequestAction.Complete, null, Captured("activationDate", DateTime.UtcNow.AddDays(-11).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))),
                new Move(admin, RequestAction.Complete)
            }, ct);
        }

        // Laptop waiting for the IT or Admin verification.
        await RunAsync("laptop", priya, new()
        {
            ["assetType"] = JsonSerializer.SerializeToElement("Laptop"),
            ["requirement"] = JsonSerializer.SerializeToElement("Replacement for a laptop that is five years old")
        }, ago(3, 4), new[] { new Move(manager, RequestAction.Approve) }, ct);

        // Asset return at the condition check, raised by someone who holds an asset.
        if (heldAsset is not null)
        {
            var code = await _employees.GetByIdAsync(heldAsset.Value.HolderId, ct);
            if (code is not null)
            {
                var holder = await _actors.ForEmployeeAsync(code.Code, ct);
                await RunAsync("asset-return", holder, new()
                {
                    ["asset"] = JsonSerializer.SerializeToElement(heldAsset.Value.Id),
                    ["reason"] = JsonSerializer.SerializeToElement("Transfer")
                }, ago(4, 0), new[] { new Move(admin, RequestAction.Approve) }, ct);
            }
        }

        // ID card waiting for HR.
        await RunAsync("id-card", priya, new()
        {
            ["requestType"] = JsonSerializer.SerializeToElement("Replacement"),
            ["reason"] = JsonSerializer.SerializeToElement("Card was lost on a site visit"),
            ["oldCardStatus"] = JsonSerializer.SerializeToElement("Lost")
        }, ago(1, 6), Array.Empty<Move>(), ct);

        // Welfare waiting for the Admin review.
        await RunAsync("welfare", priya, new()
        {
            ["category"] = JsonSerializer.SerializeToElement("Medical camp arrangements"),
            ["details"] = JsonSerializer.SerializeToElement("Annual health check camp for the office")
        }, ago(2, 8), Array.Empty<Move>(), ct);

        // Housekeeping waiting for assignment.
        if (location is not null)
        {
            await RunAsync("housekeeping", priya, new()
            {
                ["location"] = JsonSerializer.SerializeToElement(location.Value),
                ["area"] = JsonSerializer.SerializeToElement("Second floor"),
                ["category"] = JsonSerializer.SerializeToElement("Washroom"),
                ["description"] = JsonSerializer.SerializeToElement("Supplies in the washroom need refilling")
            }, ago(0, 5), Array.Empty<Move>(), ct);
        }
    }

    private static Dictionary<string, JsonElement> SimPayload(string type, string reason) =>
        new()
        {
            ["requestType"] = JsonSerializer.SerializeToElement(type),
            ["reason"] = JsonSerializer.SerializeToElement(reason)
        };

    private static Dictionary<string, JsonElement> Captured(string key, string value) =>
        new() { [key] = JsonSerializer.SerializeToElement(value) };

    private Task StationeryAsync(ActorContext requester, string item, int quantity, DateTimeOffset start, IEnumerable<Move> moves, CancellationToken ct) =>
        RunAsync("stationery", requester, new() { ["item"] = JsonSerializer.SerializeToElement(item), ["quantity"] = JsonSerializer.SerializeToElement(quantity) }, start, moves, ct);

    private Task CourierAsync(ActorContext requester, string receiver, string city, DateTimeOffset start, IEnumerable<Move> moves, CancellationToken ct) =>
        RunAsync("courier", requester, new()
        {
            ["documentDescription"] = JsonSerializer.SerializeToElement("Signed agreement"),
            ["senderName"] = JsonSerializer.SerializeToElement(requester.Name),
            ["receiverName"] = JsonSerializer.SerializeToElement(receiver),
            ["receiverAddress"] = JsonSerializer.SerializeToElement("12 Example Road"),
            ["receiverCity"] = JsonSerializer.SerializeToElement(city)
        }, start, moves, ct);

    // Creates the request at the start instant and plays each move a few hours later than the one before.
    private async Task RunAsync(string moduleCode, ActorContext requester, Dictionary<string, JsonElement> payload,
        DateTimeOffset start, IEnumerable<Move> moves, CancellationToken ct)
    {
        var definition = await _definitions.GetActiveAsync(moduleCode)
            ?? throw new InvalidOperationException($"Definition {moduleCode} is not active.");

        var at = start;
        _clock.SetOverride(at);
        var id = await _service.CreateAsync(requester, new CreateRequestCommand
        {
            ModuleCode = moduleCode,
            DefinitionId = checked((int)definition.Id),
            Payload = payload
        }, ct);

        foreach (var move in moves)
        {
            at += StepGap;
            _clock.SetOverride(at);
            long version;
            await using (var connection = await _factory.OpenAsync(ct))
            {
                version = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition("SELECT row_version FROM requests WHERE id = @Id", new { Id = id }, cancellationToken: ct));
            }
            await _service.ActAsync(move.Actor, id, new ActionCommand
            {
                Action = move.Action,
                Comment = move.Comment,
                Captured = move.Captured,
                ExpectedRowVersion = version
            }, ct);
        }
    }
}
