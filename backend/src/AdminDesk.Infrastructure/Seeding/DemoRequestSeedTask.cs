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

            // Courier requests.
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
