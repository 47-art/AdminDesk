using AdminDesk.Application.Abstractions.Persistence;
using Dapper;

namespace AdminDesk.Infrastructure.Jobs;

public sealed record ScheduledJobRow(string Id, string HandlerType, string? ArgsJson, DateTime DueUtc);

// Dapper access to scheduled_jobs. Every write runs in an immediate transaction.
public sealed class ScheduledJobRepository
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";

    private readonly IUnitOfWork _unitOfWork;

    public ScheduledJobRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task InsertAsync(string id, string handlerType, string? argsJson, DateTime dueUtc, DateTime nowUtc, CancellationToken ct) =>
        _unitOfWork.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO scheduled_jobs(id, handler_type, args_json, due_utc, state, created_utc) " +
                "VALUES(@Id, @HandlerType, @ArgsJson, @DueUtc, 'Pending', @NowUtc)",
                new { Id = id, HandlerType = handlerType, ArgsJson = argsJson, DueUtc = dueUtc, NowUtc = nowUtc },
                transaction, cancellationToken: ct));
            return 0;
        }, ct);

    // Jobs left running by a process that was killed go back to pending, so they run again.
    public Task<int> ResetRunningAsync(CancellationToken ct) =>
        _unitOfWork.ExecuteInTransactionAsync((connection, transaction) =>
            connection.ExecuteAsync(new CommandDefinition(
                "UPDATE scheduled_jobs SET state = 'Pending' WHERE state = 'Running'",
                transaction: transaction, cancellationToken: ct)), ct);

    // Marks due pending jobs as running and returns them; each job is claimed once.
    public Task<IReadOnlyList<ScheduledJobRow>> ClaimDueAsync(DateTime nowUtc, int max, CancellationToken ct) =>
        _unitOfWork.ExecuteInTransactionAsync<IReadOnlyList<ScheduledJobRow>>(async (connection, transaction) =>
        {
            var rows = (await connection.QueryAsync<ScheduledJobRow>(new CommandDefinition(
                "SELECT id AS Id, handler_type AS HandlerType, args_json AS ArgsJson, due_utc AS DueUtc " +
                "FROM scheduled_jobs WHERE state = 'Pending' AND due_utc <= @NowUtc ORDER BY due_utc LIMIT @Max",
                new { NowUtc = nowUtc, Max = max }, transaction, cancellationToken: ct))).ToList();
            foreach (var row in rows)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE scheduled_jobs SET state = 'Running' WHERE id = @Id",
                    new { row.Id }, transaction, cancellationToken: ct));
            }
            return rows;
        }, ct);

    public Task FinishAsync(string id, string state, string? error, DateTime nowUtc, CancellationToken ct) =>
        _unitOfWork.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE scheduled_jobs SET state = @State, completed_utc = @NowUtc, error = @Error WHERE id = @Id",
                new { Id = id, State = state, Error = error, NowUtc = nowUtc },
                transaction, cancellationToken: ct));
            return 0;
        }, ct);
}
