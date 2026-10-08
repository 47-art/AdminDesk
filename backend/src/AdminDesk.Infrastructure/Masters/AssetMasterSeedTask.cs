using System.Data.Common;
using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Demo;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Money;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Masters;

// Reference data for the allocation masters: about 30 SIMs, 40 laptops and other IT assets and 20 ID
// cards. Each table is filled only while it has never held a row. Everything is fixed (no randomness),
// so every fresh database gets the same records. Rows are written as the system actor.
public sealed class AssetMasterSeedTask : IStartupTask
{
    private const int SimCount = 30;
    private const int AssetCount = 40;
    private const int CardCount = 20;

    private static readonly string[] Operators = { "Jio", "Airtel", "Vi", "BSNL" };

    private static readonly (string Name, long CostMinor)[] Plans =
    {
        ("Corporate 199", 19900), ("Corporate 399", 39900), ("Corporate 599", 59900), ("Data 999", 99900)
    };

    private static readonly (string Type, string Prefix, string MakeModel)[] AssetKinds =
    {
        ("Laptop", "LAP", "Dell Latitude 5440"),
        ("Laptop", "LAP", "Lenovo ThinkPad E14"),
        ("Laptop", "LAP", "HP ProBook 450 G10"),
        ("Laptop", "LAP", "Apple MacBook Air M2")
    };

    private readonly IDbConnectionFactory _factory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuditStamper _stamper;
    private readonly ILogger<AssetMasterSeedTask> _logger;

    public AssetMasterSeedTask(
        IDbConnectionFactory factory, IUnitOfWork unitOfWork, AuditStamper stamper, ILogger<AssetMasterSeedTask> logger)
    {
        _factory = factory;
        _unitOfWork = unitOfWork;
        _stamper = stamper;
        _logger = logger;
    }

    // After the sample organisation (40) and the identity seeds, before the demo requests.
    public int Order => 50;

    public async Task RunAsync(CancellationToken ct)
    {
        bool needSims, needAssets, needCards;
        List<long> holders;
        await using (var connection = await _factory.OpenAsync(ct))
        {
            // Every row counts, retired ones included, so a table that ever held rows is never seeded again.
            needSims = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM sims", cancellationToken: ct)) == 0;
            needAssets = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM assets", cancellationToken: ct)) == 0;
            needCards = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM id_cards", cancellationToken: ct)) == 0;
            if (!needSims && !needAssets && !needCards)
            {
                return;
            }

            // The demo employee first, then everyone else in id order.
            var demo = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT id FROM employees WHERE employee_code = @Code AND " + AuditSql.Active("employees"),
                new { Code = DemoAccountCatalog.Accounts.First(a => a.Role == Roles.Employee).EmployeeCode }, cancellationToken: ct));
            var others = (await connection.QueryAsync<long>(new CommandDefinition(
                "SELECT id FROM employees WHERE " + AuditSql.Active("employees") + " AND id <> @Demo ORDER BY id",
                new { Demo = demo ?? 0 }, cancellationToken: ct))).ToList();
            holders = new List<long>();
            if (demo is { } demoId)
            {
                holders.Add(demoId);
            }
            holders.AddRange(others);
        }

        var stamp = _stamper.ForSystem();
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, tx) =>
        {
            if (needSims)
            {
                await SeedSimsAsync(connection, tx, holders, stamp, ct);
            }
            if (needAssets)
            {
                await SeedAssetsAsync(connection, tx, holders, stamp, ct);
            }
            if (needCards)
            {
                await SeedCardsAsync(connection, tx, holders, stamp, ct);
            }
            return 0;
        }, ct);

        _logger.LogInformation(
            "Allocation masters seeded (SIMs {Sims}, assets {Assets}, ID cards {Cards})", needSims, needAssets, needCards);
    }

    // The k-th holder: the first is the demo employee, the rest are spread across the others.
    private static long? HolderFor(IReadOnlyList<long> holders, int k, int stride)
    {
        if (holders.Count == 0)
        {
            return null;
        }
        return k == 0 ? holders[0] : holders[1 + (k * stride) % Math.Max(holders.Count - 1, 1)];
    }

    private static async Task SeedSimsAsync(
        DbConnection connection, DbTransaction tx, IReadOnlyList<long> holders, AuditStamp stamp, CancellationToken ct)
    {
        var rows = new List<object>();
        var history = new List<object>();
        for (var i = 0; i < SimCount; i++)
        {
            var allocated = i < 12 && holders.Count > 0;
            var holder = allocated ? HolderFor(holders, i, 7) : null;
            var plan = Plans[i % Plans.Length];
            var id = i + 1;
            rows.Add(new
            {
                Id = id,
                SimNumber = "89910000000000" + (100000 + id).ToString(System.Globalization.CultureInfo.InvariantCulture),
                MobileNumber = "9000000" + (100 + id).ToString(System.Globalization.CultureInfo.InvariantCulture),
                TelecomOperator = Operators[i % Operators.Length],
                Plan = plan.Name,
                Activation = allocated ? $"2025-{1 + i % 9:D2}-{5 + i:D2}" : null,
                Status = allocated ? SimStatuses.Allocated : SimStatuses.Available,
                Cost = plan.CostMinor,
                Holder = holder,
                stamp.CreatedUtc,
                stamp.CreatedBy,
                stamp.UpdatedUtc,
                stamp.UpdatedBy,
            });
            history.Add(History(MasterTypes.Sim, id, allocated ? MasterEvents.Allocated : MasterEvents.Added, holder, null, null, null, stamp));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO sims(id, sim_number, mobile_number, telecom_operator, plan, activation_date, status, monthly_cost_minor, " +
            "holder_employee_id, " + AuditSql.InsertColumns + ") " +
            "VALUES(@Id, @SimNumber, @MobileNumber, @TelecomOperator, @Plan, @Activation, @Status, @Cost, @Holder, " + AuditSql.InsertValues + ")",
            rows, tx, cancellationToken: ct));
        await InsertHistoryAsync(connection, tx, history, ct);
    }

    private static async Task SeedAssetsAsync(
        DbConnection connection, DbTransaction tx, IReadOnlyList<long> holders, AuditStamp stamp, CancellationToken ct)
    {
        var rows = new List<object>();
        var history = new List<object>();
        for (var i = 0; i < AssetCount; i++)
        {
            var id = i + 1;
            // 28 laptops, then monitors, docking stations and headsets.
            var (type, prefix, makeModel) = i < 28
                ? AssetKinds[i % AssetKinds.Length]
                : i < 34 ? ("Monitor", "MON", "Dell P2422H")
                : i < 38 ? ("Docking station", "DOC", "Dell WD19S")
                : ("Headset", "HDS", "Jabra Evolve2 40");
            var damaged = i is 20 or 31;
            var allocated = !damaged && i % 3 == 0 && i < 45 && holders.Count > 0 && i / 3 < 15;
            var holder = allocated ? HolderFor(holders, i / 3, 11) : null;

            var status = damaged ? AssetStatuses.Damaged : allocated ? AssetStatuses.Allocated : AssetStatuses.Available;
            rows.Add(new
            {
                Id = id,
                Tag = prefix + "-" + id.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                Type = type,
                MakeModel = makeModel,
                Serial = "SN" + (700000 + id * 37).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Status = status,
                Condition = damaged ? ItemConditions.Damaged : null,
                Holder = holder,
                stamp.CreatedUtc,
                stamp.CreatedBy,
                stamp.UpdatedUtc,
                stamp.UpdatedBy,
            });
            history.Add(damaged
                ? History(MasterTypes.Asset, id, MasterEvents.Returned, null, ItemConditions.Damaged, MoneyConverter.ToMinor(2500m), null, stamp)
                : History(MasterTypes.Asset, id, allocated ? MasterEvents.Allocated : MasterEvents.Added, holder, null, null, null, stamp));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO assets(id, asset_tag, asset_type, make_model, serial_number, status, item_condition, holder_employee_id, " +
            AuditSql.InsertColumns + ") " +
            "VALUES(@Id, @Tag, @Type, @MakeModel, @Serial, @Status, @Condition, @Holder, " + AuditSql.InsertValues + ")",
            rows, tx, cancellationToken: ct));
        await InsertHistoryAsync(connection, tx, history, ct);
    }

    private static async Task SeedCardsAsync(
        DbConnection connection, DbTransaction tx, IReadOnlyList<long> holders, AuditStamp stamp, CancellationToken ct)
    {
        // A card needs an employee, so nothing is seeded on a database without any.
        var count = Math.Min(CardCount, holders.Count);
        var rows = new List<object>();
        var history = new List<object>();
        for (var i = 0; i < count; i++)
        {
            var id = i + 1;
            var holder = HolderFor(holders, i, 9)!.Value;
            rows.Add(new
            {
                Id = id,
                Number = "IDC-" + id.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                EmployeeId = holder,
                Status = IdCardStatuses.Active,
                Issued = $"2024-{1 + i % 12:D2}-{1 + i:D2}",
                stamp.CreatedUtc,
                stamp.CreatedBy,
                stamp.UpdatedUtc,
                stamp.UpdatedBy,
            });
            history.Add(History(MasterTypes.IdCard, id, MasterEvents.Issued, holder, null, null, null, stamp));
        }

        if (rows.Count == 0)
        {
            return;
        }
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO id_cards(id, card_number, employee_id, status, issued_date, " + AuditSql.InsertColumns + ") " +
            "VALUES(@Id, @Number, @EmployeeId, @Status, @Issued, " + AuditSql.InsertValues + ")",
            rows, tx, cancellationToken: ct));
        await InsertHistoryAsync(connection, tx, history, ct);
    }

    private static object History(
        string type, long masterId, string eventType, long? employeeId, string? condition, long? costMinor, string? notes, AuditStamp stamp) =>
        new
        {
            Type = type,
            MasterId = masterId,
            Event = eventType,
            EmployeeId = employeeId,
            Condition = condition,
            Cost = costMinor,
            Notes = notes,
            stamp.CreatedUtc,
            stamp.CreatedBy,
            stamp.UpdatedUtc,
            stamp.UpdatedBy,
        };

    private static Task InsertHistoryAsync(DbConnection connection, DbTransaction tx, List<object> rows, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO master_history(master_type, master_id, event_type, employee_id, item_condition, cost_minor, notes, event_utc, " +
            AuditSql.InsertColumns + ") VALUES(@Type, @MasterId, @Event, @EmployeeId, @Condition, @Cost, @Notes, @CreatedUtc, " +
            AuditSql.InsertValues + ")",
            rows, tx, cancellationToken: ct));
}
