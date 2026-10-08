using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Infrastructure.Persistence;
using AdminDesk.SharedKernel.Constants;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AdminDesk.Infrastructure.Seeding;

// Creates the sample organisation on a database that has never held an employee,
// and only when demo mode is on. Rows are written in one transaction as the system actor.
public sealed class OrganisationSeedTask : IStartupTask
{
    private readonly IConfiguration _configuration;
    private readonly IDbConnectionFactory _factory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuditStamper _stamper;
    private readonly ILogger<OrganisationSeedTask> _logger;

    public OrganisationSeedTask(
        IConfiguration configuration,
        IDbConnectionFactory factory,
        IUnitOfWork unitOfWork,
        AuditStamper stamper,
        ILogger<OrganisationSeedTask> logger)
    {
        _configuration = configuration;
        _factory = factory;
        _unitOfWork = unitOfWork;
        _stamper = stamper;
        _logger = logger;
    }

    public int Order => 40;

    public async Task RunAsync(CancellationToken ct)
    {
        if (!bool.TryParse(_configuration[ConfigKeys.DemoEnabled], out var enabled) || !enabled)
        {
            _logger.LogInformation("Demo data disabled, organisation not seeded");
            return;
        }

        // Counts every row, removed ones included, so a database that ever held
        // employees is never seeded again.
        int existing;
        await using (var connection = await _factory.OpenAsync(ct))
        {
            existing = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(*) FROM employees", cancellationToken: ct));
        }
        if (existing > 0)
        {
            _logger.LogInformation("Employees already present, organisation not seeded");
            return;
        }

        var organisation = SampleOrganisationGenerator.Generate();
        var stamp = _stamper.ForSystem();

        await _unitOfWork.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            await InsertMastersAsync(connection, transaction, "departments", organisation.Departments, stamp, ct);
            await InsertMastersAsync(connection, transaction, "locations", organisation.Locations, stamp, ct);
            await InsertMastersAsync(connection, transaction, "projects", organisation.Projects, stamp, ct);
            await InsertMastersAsync(connection, transaction, "cost_centres", organisation.CostCentres, stamp, ct);

            var departments = organisation.Departments.ToDictionary(d => d.Code, d => d.Id);
            var locations = organisation.Locations.ToDictionary(l => l.Code, l => l.Id);
            var ids = organisation.Employees.ToDictionary(e => e.Code, e => e.Id);

            // Level by level, so a manager row always exists before the people reporting to it.
            foreach (var level in organisation.Employees.GroupBy(e => e.Depth).OrderBy(g => g.Key))
            {
                var rows = level.Select(e => new
                {
                    e.Id,
                    e.Code,
                    e.FullName,
                    e.Email,
                    e.Designation,
                    DepartmentId = departments[e.DepartmentCode],
                    LocationId = locations[e.LocationCode],
                    ManagerId = e.ManagerCode is null ? (long?)null : ids[e.ManagerCode],
                    stamp.CreatedUtc,
                    stamp.CreatedBy,
                    stamp.UpdatedUtc,
                    stamp.UpdatedBy,
                }).ToList();

                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO employees(id, employee_code, full_name, email, designation, department_id, location_id, " +
                    "reporting_manager_id, " + AuditSql.InsertColumns + ") " +
                    "VALUES(@Id, @Code, @FullName, @Email, @Designation, @DepartmentId, @LocationId, @ManagerId, " +
                    AuditSql.InsertValues + ")",
                    rows, transaction, cancellationToken: ct));
            }
            return 0;
        }, ct);

        _logger.LogInformation(
            "Sample organisation seeded: {Employees} employees, {Departments} departments, {Locations} locations, {Projects} projects, {CostCentres} cost centres",
            organisation.Employees.Count, organisation.Departments.Count, organisation.Locations.Count,
            organisation.Projects.Count, organisation.CostCentres.Count);
    }

    private static Task InsertMastersAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string table,
        IReadOnlyList<SeedMaster> rows,
        AuditStamp stamp,
        CancellationToken ct)
    {
        // The table name comes from the fixed calls above, never from input.
        var sql = "INSERT INTO " + table + "(id, code, name, " + AuditSql.InsertColumns + ") " +
                  "VALUES(@Id, @Code, @Name, " + AuditSql.InsertValues + ")";
        var parameters = rows.Select(r => new
        {
            r.Id,
            r.Code,
            r.Name,
            stamp.CreatedUtc,
            stamp.CreatedBy,
            stamp.UpdatedUtc,
            stamp.UpdatedBy,
        }).ToList();
        return connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
    }
}
