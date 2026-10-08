using System.Text.Json;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.Application.Jobs;
using Dapper;

namespace AdminDesk.Infrastructure.Jobs.Handlers;

public sealed record SpikeWriteArgs(string Label);

// Writes one row to job_probe_log and then reads a count, so several of these running
// together exercise the single writer next to the log sink and request traffic.
public sealed class SpikeWriteJobHandler : IJobHandler
{
    public const string Source = "spike";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDbConnectionFactory _connections;
    private readonly TimeProvider _clock;

    public SpikeWriteJobHandler(IUnitOfWork unitOfWork, IDbConnectionFactory connections, TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _connections = connections;
        _clock = clock;
    }

    public async Task RunAsync(JobContext context, CancellationToken ct)
    {
        var args = context.ArgsJson is null ? null : JsonSerializer.Deserialize<SpikeWriteArgs>(context.ArgsJson);
        var label = args?.Label ?? "none";
        var now = _clock.GetUtcNow().UtcDateTime;

        await _unitOfWork.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO job_probe_log(source, handler, worker_label, written_utc) VALUES(@Source, @Handler, @Label, @Now)",
                new { Source, Handler = nameof(SpikeWriteJobHandler), Label = label, Now = now },
                transaction, cancellationToken: ct));
            return 0;
        }, ct);

        await using var read = await _connections.OpenAsync(ct);
        await read.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM job_probe_log", cancellationToken: ct));
    }
}
