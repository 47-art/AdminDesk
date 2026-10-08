using System.Data.Common;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Masters;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Money;
using Dapper;

namespace AdminDesk.Infrastructure.Masters;

// Dapper access to the SIM, asset and ID card masters and their history. Table and column names
// are constants in this class; every value goes in as a parameter. Retiring a row is is_active = 0.
public sealed class MasterAssetRepository : IMasterAssetRepository
{
    private const string SimSelect =
        "SELECT s.id AS Id, s.sim_number AS SimNumber, s.mobile_number AS MobileNumber, s.telecom_operator AS TelecomOperator, " +
        "s.plan AS Plan, s.activation_date AS ActivationDate, s.status AS Status, s.monthly_cost_minor AS MonthlyCostMinor, " +
        "s.holder_employee_id AS HolderEmployeeId, e.full_name AS HolderName, e.employee_code AS HolderCode, " +
        "s.deleted_utc AS RetiredUtc, CASE WHEN s.is_active = 1 AND s.deleted_utc IS NULL THEN 0 ELSE 1 END AS Retired " +
        "FROM sims s LEFT JOIN employees e ON e.id = s.holder_employee_id ";

    private const string AssetSelect =
        "SELECT a.id AS Id, a.asset_tag AS AssetTag, a.asset_type AS AssetType, a.make_model AS MakeModel, " +
        "a.serial_number AS SerialNumber, a.status AS Status, a.item_condition AS Condition, " +
        "a.holder_employee_id AS HolderEmployeeId, e.full_name AS HolderName, e.employee_code AS HolderCode, " +
        "a.deleted_utc AS RetiredUtc, CASE WHEN a.is_active = 1 AND a.deleted_utc IS NULL THEN 0 ELSE 1 END AS Retired " +
        "FROM assets a LEFT JOIN employees e ON e.id = a.holder_employee_id ";

    private const string CardSelect =
        "SELECT c.id AS Id, c.card_number AS CardNumber, c.employee_id AS EmployeeId, e.full_name AS EmployeeName, " +
        "e.employee_code AS EmployeeCode, c.status AS Status, c.issued_date AS IssuedDate, " +
        "c.deleted_utc AS RetiredUtc, CASE WHEN c.is_active = 1 AND c.deleted_utc IS NULL THEN 0 ELSE 1 END AS Retired " +
        "FROM id_cards c LEFT JOIN employees e ON e.id = c.employee_id ";

    private readonly IDbConnectionFactory _factory;
    private readonly AuditStamper _stamper;
    private readonly ISqlDialect _dialect;

    public MasterAssetRepository(IDbConnectionFactory factory, AuditStamper stamper, ISqlDialect dialect)
    {
        _factory = factory;
        _stamper = stamper;
        _dialect = dialect;
    }

    private sealed class SimRow
    {
        public long Id { get; set; }
        public string SimNumber { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string TelecomOperator { get; set; } = string.Empty;
        public string Plan { get; set; } = string.Empty;
        public DateOnly? ActivationDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public long MonthlyCostMinor { get; set; }
        public long? HolderEmployeeId { get; set; }
        public string? HolderName { get; set; }
        public string? HolderCode { get; set; }
        public long Retired { get; set; }
        public DateTime? RetiredUtc { get; set; }

        public SimDto ToDto() => new(
            Id, SimNumber, MobileNumber, TelecomOperator, Plan, ActivationDate, Status,
            MoneyConverter.ToRupees(MonthlyCostMinor), HolderEmployeeId, HolderName, HolderCode, Retired != 0, RetiredUtc);
    }

    private sealed class AssetRow
    {
        public long Id { get; set; }
        public string AssetTag { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;
        public string MakeModel { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Condition { get; set; }
        public long? HolderEmployeeId { get; set; }
        public string? HolderName { get; set; }
        public string? HolderCode { get; set; }
        public long Retired { get; set; }
        public DateTime? RetiredUtc { get; set; }

        public AssetDto ToDto() => new(
            Id, AssetTag, AssetType, MakeModel, SerialNumber, Status, Condition, HolderEmployeeId, HolderName, HolderCode,
            Retired != 0, RetiredUtc);
    }

    private sealed class CardRow
    {
        public long Id { get; set; }
        public string CardNumber { get; set; } = string.Empty;
        public long EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public string? EmployeeCode { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateOnly IssuedDate { get; set; }
        public long Retired { get; set; }
        public DateTime? RetiredUtc { get; set; }

        public IdCardDto ToDto() => new(
            Id, CardNumber, EmployeeId, EmployeeName, EmployeeCode, Status, IssuedDate, Retired != 0, RetiredUtc);
    }

    private sealed class HistoryRow
    {
        public long Id { get; set; }
        public string EventType { get; set; } = string.Empty;
        public long? EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public string? EmployeeCode { get; set; }
        public long? RequestId { get; set; }
        public string? RequestNo { get; set; }
        public string? Condition { get; set; }
        public long? CostMinor { get; set; }
        public string? Notes { get; set; }
        public DateTime EventUtc { get; set; }
    }

    private sealed class HoldingRow
    {
        public long Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Since { get; set; } = string.Empty;
    }

    // ------------------------------------------------------------------ lists

    // A fragment of "AND ..." conditions shared by the three lists; the search columns are constants.
    private string Filters(MasterListQuery q, string alias, string[] searchColumns, DynamicParameters p)
    {
        var sql = q.IncludeRetired ? string.Empty : " AND " + AuditSql.Active(alias);
        if (q.Status is not null)
        {
            sql += $" AND {alias}.status = @Status";
            p.Add("Status", q.Status);
        }
        if (q.HolderEmployeeId is { } holder)
        {
            sql += alias == "c" ? " AND c.employee_id = @Holder" : $" AND {alias}.holder_employee_id = @Holder";
            p.Add("Holder", holder);
        }
        if (q.Search is not null)
        {
            var clauses = searchColumns.Select(c => _dialect.Like(c, "@Q")).ToList();
            clauses.Add(_dialect.Like("e.full_name", "@Q"));
            clauses.Add(_dialect.Like("e.employee_code", "@Q"));
            sql += " AND (" + string.Join(" OR ", clauses) + ")";
            p.Add("Q", "%" + _dialect.EscapeLikeValue(q.Search) + "%");
        }
        return sql;
    }

    private async Task<PagedResult<T>> PageAsync<T>(
        string select, string countFrom, string alias, string[] searchColumns, string orderBy,
        MasterListQuery q, Func<T, T>? map, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = " WHERE 1 = 1" + Filters(q, alias, searchColumns, p);
        p.Add("Limit", q.PageSize);
        p.Add("Offset", (q.Page - 1) * q.PageSize);

        await using var connection = await _factory.OpenAsync(ct);
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) " + countFrom + where, p, cancellationToken: ct));
        var rows = await connection.QueryAsync<T>(new CommandDefinition(
            select + where + " ORDER BY " + orderBy + " " + _dialect.LimitOffset("@Limit", "@Offset"), p, cancellationToken: ct));
        return new PagedResult<T>(rows.Select(r => map is null ? r : map(r)).ToList(), total, q.Page, q.PageSize);
    }

    public async Task<PagedResult<SimDto>> ListSimsAsync(MasterListQuery query, CancellationToken ct)
    {
        var page = await PageAsync<SimRow>(
            SimSelect, "FROM sims s LEFT JOIN employees e ON e.id = s.holder_employee_id", "s",
            new[] { "s.sim_number", "s.mobile_number", "s.telecom_operator", "s.plan" }, "s.sim_number, s.id", query, null, ct);
        return new PagedResult<SimDto>(page.Items.Select(r => r.ToDto()).ToList(), page.Total, page.Page, page.PageSize);
    }

    public async Task<PagedResult<AssetDto>> ListAssetsAsync(MasterListQuery query, CancellationToken ct)
    {
        var page = await PageAsync<AssetRow>(
            AssetSelect, "FROM assets a LEFT JOIN employees e ON e.id = a.holder_employee_id", "a",
            new[] { "a.asset_tag", "a.asset_type", "a.make_model", "a.serial_number" }, "a.asset_tag, a.id", query, null, ct);
        return new PagedResult<AssetDto>(page.Items.Select(r => r.ToDto()).ToList(), page.Total, page.Page, page.PageSize);
    }

    public async Task<PagedResult<IdCardDto>> ListIdCardsAsync(MasterListQuery query, CancellationToken ct)
    {
        var page = await PageAsync<CardRow>(
            CardSelect, "FROM id_cards c LEFT JOIN employees e ON e.id = c.employee_id", "c",
            new[] { "c.card_number" }, "c.card_number, c.id", query, null, ct);
        return new PagedResult<IdCardDto>(page.Items.Select(r => r.ToDto()).ToList(), page.Total, page.Page, page.PageSize);
    }

    public async Task<IReadOnlyList<MasterHistoryDto>> GetHistoryAsync(string masterType, long id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            "SELECT h.id AS Id, h.event_type AS EventType, h.employee_id AS EmployeeId, e.full_name AS EmployeeName, " +
            "e.employee_code AS EmployeeCode, h.request_id AS RequestId, r.request_no AS RequestNo, " +
            "h.item_condition AS Condition, h.cost_minor AS CostMinor, h.notes AS Notes, h.event_utc AS EventUtc " +
            "FROM master_history h LEFT JOIN employees e ON e.id = h.employee_id LEFT JOIN requests r ON r.id = h.request_id " +
            "WHERE h.master_type = @Type AND h.master_id = @Id AND " + AuditSql.Active("h") +
            " ORDER BY h.event_utc DESC, h.id DESC",
            new { Type = masterType, Id = id }, cancellationToken: ct));
        return rows.Select(r => new MasterHistoryDto(
            r.Id, r.EventType, r.EmployeeId, r.EmployeeName, r.EmployeeCode, r.RequestId, r.RequestNo, r.Condition,
            r.CostMinor is { } cost ? MoneyConverter.ToRupees(cost) : null, r.Notes, r.EventUtc)).ToList();
    }

    public async Task<bool> ExistsAsync(string masterType, long id, CancellationToken ct)
    {
        var table = masterType switch
        {
            MasterTypes.Sim => "sims",
            MasterTypes.Asset => "assets",
            MasterTypes.IdCard => "id_cards",
            _ => null
        };
        if (table is null)
        {
            return false;
        }
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM " + table + " t WHERE t.id = @Id",
            new { Id = id }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> IsRetiredAsync(DbTransaction tx, string masterType, long id, CancellationToken ct)
    {
        var table = masterType switch
        {
            MasterTypes.Sim => "sims",
            MasterTypes.Asset => "assets",
            MasterTypes.IdCard => "id_cards",
            _ => null
        };
        if (table is null)
        {
            return false;
        }
        return await tx.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM " + table + " t WHERE t.id = @Id AND NOT (" + AuditSql.Active("t") + ")",
            new { Id = id }, tx, cancellationToken: ct)) > 0;
    }

    public async Task<HoldingsDto> ListHeldByAsync(long employeeId, CancellationToken ct)
    {
        // Date a holder took the item: the latest history row for that holder that is not a return.
        static string Since(string type, string alias, string holderColumn) =>
            "COALESCE((SELECT substr(MAX(h.event_utc), 1, 10) FROM master_history h WHERE h.master_type = '" + type +
            "' AND h.master_id = " + alias + ".id AND h.employee_id = " + holderColumn + " AND h.event_type <> '" +
            MasterEvents.Returned + "'), substr(" + alias + ".updated_utc, 1, 10))";

        var sql =
            "SELECT s.id AS Id, s.sim_number || ' - ' || s.mobile_number AS Label, " + Since(MasterTypes.Sim, "s", "s.holder_employee_id") + " AS Since " +
            "FROM sims s WHERE s.holder_employee_id = @Id AND s.status = @SimStatus AND " + AuditSql.Active("s") + " ORDER BY s.sim_number; " +
            "SELECT a.id AS Id, a.asset_tag || ' - ' || a.make_model AS Label, " + Since(MasterTypes.Asset, "a", "a.holder_employee_id") + " AS Since " +
            "FROM assets a WHERE a.holder_employee_id = @Id AND a.status = @AssetStatus AND " + AuditSql.Active("a") + " ORDER BY a.asset_tag; " +
            "SELECT c.id AS Id, c.card_number AS Label, c.issued_date AS Since " +
            "FROM id_cards c WHERE c.employee_id = @Id AND c.status = @CardStatus AND " + AuditSql.Active("c") + " ORDER BY c.id";

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new { Id = employeeId, SimStatus = SimStatuses.Allocated, AssetStatus = AssetStatuses.Allocated, CardStatus = IdCardStatuses.Active },
            cancellationToken: ct));
        var sims = (await multi.ReadAsync<HoldingRow>()).Select(r => new HoldingDto(MasterTypes.Sim, r.Id, r.Label, r.Since)).ToList();
        var assets = (await multi.ReadAsync<HoldingRow>()).Select(r => new HoldingDto(MasterTypes.Asset, r.Id, r.Label, r.Since)).ToList();
        var card = (await multi.ReadAsync<HoldingRow>()).Select(r => new HoldingDto(MasterTypes.IdCard, r.Id, r.Label, r.Since)).FirstOrDefault();
        return new HoldingsDto(sims, assets, card);
    }

    // ------------------------------------------------------- single rows

    public async Task<SimDto?> GetSimAsync(DbTransaction tx, long id, CancellationToken ct) =>
        (await tx.Connection!.QueryFirstOrDefaultAsync<SimRow>(new CommandDefinition(
            SimSelect + "WHERE s.id = @Id AND " + AuditSql.Active("s"), new { Id = id }, tx, cancellationToken: ct)))?.ToDto();

    public async Task<AssetDto?> GetAssetAsync(DbTransaction tx, long id, CancellationToken ct) =>
        (await tx.Connection!.QueryFirstOrDefaultAsync<AssetRow>(new CommandDefinition(
            AssetSelect + "WHERE a.id = @Id AND " + AuditSql.Active("a"), new { Id = id }, tx, cancellationToken: ct)))?.ToDto();

    public async Task<IdCardDto?> GetIdCardAsync(DbTransaction tx, long id, CancellationToken ct) =>
        (await tx.Connection!.QueryFirstOrDefaultAsync<CardRow>(new CommandDefinition(
            CardSelect + "WHERE c.id = @Id AND " + AuditSql.Active("c"), new { Id = id }, tx, cancellationToken: ct)))?.ToDto();

    // Retired rows count: a number that was ever used stays reserved.
    public async Task<bool> IsNumberTakenAsync(
        DbTransaction tx, MasterNumberKind kind, string value, long? exceptId, CancellationToken ct)
    {
        var (table, column) = kind switch
        {
            MasterNumberKind.SimNumber => ("sims", "sim_number"),
            MasterNumberKind.MobileNumber => ("sims", "mobile_number"),
            MasterNumberKind.AssetTag => ("assets", "asset_tag"),
            MasterNumberKind.SerialNumber => ("assets", "serial_number"),
            _ => ("id_cards", "card_number")
        };
        var count = await tx.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM " + table + " t WHERE " + _dialect.EqualsIgnoreCase("TRIM(t." + column + ")", "@Value") +
            " AND (@Except IS NULL OR t.id <> @Except)",
            new { Value = value.Trim(), Except = exceptId }, tx, cancellationToken: ct));
        return count > 0;
    }

    public async Task<bool> HasActiveIdCardAsync(DbTransaction tx, long employeeId, CancellationToken ct) =>
        await tx.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM id_cards c WHERE c.employee_id = @Id AND c.status = @Status AND " + AuditSql.Active("c"),
            new { Id = employeeId, Status = IdCardStatuses.Active }, tx, cancellationToken: ct)) > 0;

    // ----------------------------------------------------- owner maintenance

    // Two saves at the same moment can both pass the in-use check; the database then refuses the second
    // one, and that is reported as the same field message as the check.
    private async Task<T> Guard<T>(Func<Task<T>> write)
    {
        try
        {
            return await write();
        }
        catch (Exception exception) when (_dialect.IsUniqueViolation(exception))
        {
            throw DuplicateNumber(exception.Message);
        }
    }

    private static ValidationException DuplicateNumber(string detail)
    {
        var (field, message) =
            detail.Contains("mobile_number", StringComparison.OrdinalIgnoreCase) ? ("mobileNumber", "This mobile number is already in use.")
            : detail.Contains("sim_number", StringComparison.OrdinalIgnoreCase) ? ("simNumber", "This SIM number is already in use.")
            : detail.Contains("serial_number", StringComparison.OrdinalIgnoreCase) ? ("serialNumber", "This serial number is already in use.")
            : detail.Contains("asset_tag", StringComparison.OrdinalIgnoreCase) ? ("assetTag", "This asset tag is already in use.")
            : ("cardNumber", "This card number is already in use.");
        return new ValidationException(field, message);
    }

    public Task<long> InsertSimAsync(DbTransaction tx, SimFields f, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForCreate());
        p.Add("SimNumber", f.SimNumber);
        p.Add("MobileNumber", f.MobileNumber);
        p.Add("TelecomOperator", f.TelecomOperator);
        p.Add("Plan", f.Plan);
        p.Add("Cost", f.MonthlyCostMinor);
        p.Add("Status", SimStatuses.Available);
        return Guard(() => tx.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            _dialect.InsertReturningId(
                "INSERT INTO sims(sim_number, mobile_number, telecom_operator, plan, status, monthly_cost_minor, " + AuditSql.InsertColumns + ") " +
                "VALUES(@SimNumber, @MobileNumber, @TelecomOperator, @Plan, @Status, @Cost, " + AuditSql.InsertValues + ")"),
            p, tx, cancellationToken: ct)));
    }

    public Task<long> InsertAssetAsync(DbTransaction tx, AssetFields f, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForCreate());
        p.Add("Tag", f.AssetTag);
        p.Add("Type", f.AssetType);
        p.Add("MakeModel", f.MakeModel);
        p.Add("Serial", f.SerialNumber);
        p.Add("Status", AssetStatuses.Available);
        return Guard(() => tx.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            _dialect.InsertReturningId(
                "INSERT INTO assets(asset_tag, asset_type, make_model, serial_number, status, " + AuditSql.InsertColumns + ") " +
                "VALUES(@Tag, @Type, @MakeModel, @Serial, @Status, " + AuditSql.InsertValues + ")"),
            p, tx, cancellationToken: ct)));
    }

    public Task<long> InsertIdCardAsync(DbTransaction tx, string cardNumber, long employeeId, DateOnly issuedDate, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForCreate());
        p.Add("Number", cardNumber);
        p.Add("EmployeeId", employeeId);
        p.Add("Issued", issuedDate);
        p.Add("Status", IdCardStatuses.Active);
        return Guard(() => tx.Connection!.ExecuteScalarAsync<long>(new CommandDefinition(
            _dialect.InsertReturningId(
                "INSERT INTO id_cards(card_number, employee_id, status, issued_date, " + AuditSql.InsertColumns + ") " +
                "VALUES(@Number, @EmployeeId, @Status, @Issued, " + AuditSql.InsertValues + ")"),
            p, tx, cancellationToken: ct)));
    }

    public Task UpdateSimAsync(DbTransaction tx, long id, SimFields f, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", id);
        p.Add("SimNumber", f.SimNumber);
        p.Add("MobileNumber", f.MobileNumber);
        p.Add("TelecomOperator", f.TelecomOperator);
        p.Add("Plan", f.Plan);
        p.Add("Cost", f.MonthlyCostMinor);
        return Guard(() => tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE sims SET sim_number = @SimNumber, mobile_number = @MobileNumber, telecom_operator = @TelecomOperator, " +
            "plan = @Plan, monthly_cost_minor = @Cost, " + AuditSql.UpdateSet + " WHERE id = @Id AND " + AuditSql.Active("sims"),
            p, tx, cancellationToken: ct)));
    }

    public Task UpdateAssetAsync(DbTransaction tx, long id, AssetFields f, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", id);
        p.Add("Tag", f.AssetTag);
        p.Add("Type", f.AssetType);
        p.Add("MakeModel", f.MakeModel);
        p.Add("Serial", f.SerialNumber);
        return Guard(() => tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE assets SET asset_tag = @Tag, asset_type = @Type, make_model = @MakeModel, serial_number = @Serial, " +
            AuditSql.UpdateSet + " WHERE id = @Id AND " + AuditSql.Active("assets"),
            p, tx, cancellationToken: ct)));
    }

    public Task UpdateIdCardAsync(DbTransaction tx, long id, IdCardFields f, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", id);
        p.Add("Number", f.CardNumber);
        p.Add("Issued", f.IssuedDate);
        return Guard(() => tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE id_cards SET card_number = @Number, issued_date = @Issued, " + AuditSql.UpdateSet +
            " WHERE id = @Id AND " + AuditSql.Active("id_cards"),
            p, tx, cancellationToken: ct)));
    }

    public async Task<bool> RetireAsync(DbTransaction tx, string masterType, long id, CancellationToken ct)
    {
        // Held means: a holder on a SIM or asset, an active card.
        var (table, notHeld) = masterType switch
        {
            MasterTypes.Sim => ("sims", "holder_employee_id IS NULL"),
            MasterTypes.Asset => ("assets", "holder_employee_id IS NULL"),
            MasterTypes.IdCard => ("id_cards", "status <> '" + IdCardStatuses.Active + "'"),
            _ => throw new ArgumentOutOfRangeException(nameof(masterType))
        };
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", id);
        var rows = await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE " + table + " SET " + AuditSql.SoftDeleteSet + " WHERE id = @Id AND " + notHeld + " AND " + AuditSql.Active(table),
            p, tx, cancellationToken: ct));
        return rows > 0;
    }

    // ------------------------------------------------------ request effects

    public async Task<bool> AllocateSimAsync(DbTransaction tx, long simId, long employeeId, DateOnly? activationDate, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", simId);
        p.Add("Holder", employeeId);
        p.Add("Activation", activationDate);
        p.Add("From", SimStatuses.Available);
        p.Add("To", SimStatuses.Allocated);
        return await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE sims SET status = @To, holder_employee_id = @Holder, activation_date = COALESCE(@Activation, activation_date), " +
            AuditSql.UpdateSet + " WHERE id = @Id AND status = @From AND " + AuditSql.Active("sims"),
            p, tx, cancellationToken: ct)) > 0;
    }

    public async Task<bool> ReleaseSimAsync(DbTransaction tx, long simId, long fromEmployeeId, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", simId);
        p.Add("Holder", fromEmployeeId);
        p.Add("From", SimStatuses.Allocated);
        p.Add("To", SimStatuses.Available);
        return await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE sims SET status = @To, holder_employee_id = NULL, " + AuditSql.UpdateSet +
            " WHERE id = @Id AND status = @From AND holder_employee_id = @Holder AND " + AuditSql.Active("sims"),
            p, tx, cancellationToken: ct)) > 0;
    }

    public async Task<bool> TransferSimAsync(DbTransaction tx, long simId, long fromEmployeeId, long toEmployeeId, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", simId);
        p.Add("From", fromEmployeeId);
        p.Add("To", toEmployeeId);
        p.Add("Status", SimStatuses.Allocated);
        return await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE sims SET holder_employee_id = @To, " + AuditSql.UpdateSet +
            " WHERE id = @Id AND status = @Status AND holder_employee_id = @From AND " + AuditSql.Active("sims"),
            p, tx, cancellationToken: ct)) > 0;
    }

    public async Task<bool> AllocateAssetAsync(DbTransaction tx, long assetId, long employeeId, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", assetId);
        p.Add("Holder", employeeId);
        p.Add("From", AssetStatuses.Available);
        p.Add("To", AssetStatuses.Allocated);
        return await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE assets SET status = @To, holder_employee_id = @Holder, " + AuditSql.UpdateSet +
            " WHERE id = @Id AND status = @From AND " + AuditSql.Active("assets"),
            p, tx, cancellationToken: ct)) > 0;
    }

    public async Task<bool> ReleaseAssetAsync(
        DbTransaction tx, long assetId, long fromEmployeeId, string newStatus, string condition, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Id", assetId);
        p.Add("Holder", fromEmployeeId);
        p.Add("From", AssetStatuses.Allocated);
        p.Add("To", newStatus);
        p.Add("Condition", condition);
        return await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE assets SET status = @To, item_condition = @Condition, holder_employee_id = NULL, " + AuditSql.UpdateSet +
            " WHERE id = @Id AND status = @From AND holder_employee_id = @Holder AND " + AuditSql.Active("assets"),
            p, tx, cancellationToken: ct)) > 0;
    }

    public async Task<IReadOnlyList<long>> ReplaceActiveIdCardsAsync(DbTransaction tx, long employeeId, CancellationToken ct)
    {
        var ids = (await tx.Connection!.QueryAsync<long>(new CommandDefinition(
            "SELECT c.id FROM id_cards c WHERE c.employee_id = @Id AND c.status = @Active AND " + AuditSql.Active("c") + " ORDER BY c.id",
            new { Id = employeeId, Active = IdCardStatuses.Active }, tx, cancellationToken: ct))).ToList();
        if (ids.Count == 0)
        {
            return ids;
        }
        var p = new DynamicParameters(_stamper.ForUpdate());
        p.Add("Ids", ids);
        p.Add("Replaced", IdCardStatuses.Replaced);
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "UPDATE id_cards SET status = @Replaced, " + AuditSql.UpdateSet + " WHERE id IN @Ids",
            p, tx, cancellationToken: ct));
        return ids;
    }

    public Task AppendHistoryAsync(DbTransaction tx, HistoryEntry e, CancellationToken ct)
    {
        var p = new DynamicParameters(_stamper.ForCreate());
        p.Add("Type", e.MasterType);
        p.Add("MasterId", e.MasterId);
        p.Add("Event", e.EventType);
        p.Add("EmployeeId", e.EmployeeId);
        p.Add("RequestId", e.RequestId);
        p.Add("Condition", e.Condition);
        p.Add("Cost", e.CostMinor);
        p.Add("Notes", e.Notes);
        return tx.Connection!.ExecuteAsync(new CommandDefinition(
            "INSERT INTO master_history(master_type, master_id, event_type, employee_id, request_id, item_condition, cost_minor, notes, event_utc, " +
            AuditSql.InsertColumns + ") VALUES(@Type, @MasterId, @Event, @EmployeeId, @RequestId, @Condition, @Cost, @Notes, @CreatedUtc, " +
            AuditSql.InsertValues + ")",
            p, tx, cancellationToken: ct));
    }
}
