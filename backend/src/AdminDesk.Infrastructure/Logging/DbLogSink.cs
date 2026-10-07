using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AdminDesk.SharedKernel.Constants;
using Dapper;
using Microsoft.Data.Sqlite;
using Serilog.Core;
using Serilog.Events;

namespace AdminDesk.Infrastructure.Logging;

// Writes log events to the logs table in batches from one background task, on its own
// short-lived connection. Emit never throws and never blocks; a full queue drops the
// newest events, and any write failure drops that batch and is reported once per
// failure streak.
public sealed class DbLogSink : ILogEventSink, IDisposable
{
    private const int MaxMessageLength = 8000;
    private const int MaxExceptionLength = 16000;
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    private static readonly string OwnNamespace = typeof(DbLogSink).Namespace!;
    private static readonly AsyncLocal<bool> InsideSink = new();

    private static readonly string InsertSql =
        $"INSERT INTO {LogConstants.Table} " +
        $"({LogConstants.TimestampUtc}, {LogConstants.Level}, {LogConstants.Category}, {LogConstants.Message}, " +
        $"{LogConstants.MessageTemplate}, {LogConstants.Exception}, {LogConstants.CorrelationId}, {LogConstants.PropertiesJson}) " +
        "VALUES (@TimestampUtc, @Level, @Category, @Message, @MessageTemplate, @Exception, @CorrelationId, @PropertiesJson)";

    private readonly string _connectionString;
    private readonly Action<string, Exception?> _reportFailure;
    private readonly Channel<LogEvent> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly EventHandler _processExit;

    private long _dropped;
    private long _pending;
    private bool _failing;
    private int _disposed;

    public DbLogSink(string connectionString, Action<string, Exception?> reportFailure)
    {
        _connectionString = connectionString;
        _reportFailure = reportFailure;
        _queue = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(LogConstants.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Factory.StartNew(
            RunAsync,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        _processExit = (_, _) => Dispose();
        AppDomain.CurrentDomain.ProcessExit += _processExit;
    }

    public void Emit(LogEvent logEvent)
    {
        try
        {
            if (InsideSink.Value || ShouldSkip(logEvent))
            {
                return;
            }
            Interlocked.Increment(ref _pending);
            if (!_queue.Writer.TryWrite(logEvent))
            {
                Interlocked.Decrement(ref _pending);
                Interlocked.Increment(ref _dropped);
            }
        }
        catch
        {
            // Logging must never break the caller.
        }
    }

    // Waits until everything queued so far has been written or dropped, up to the timeout.
    // Does nothing while the table is not ready.
    public async Task FlushAsync(TimeSpan timeout)
    {
        try
        {
            if (!LogDatabaseGate.IsOpen)
            {
                return;
            }
            using var cts = new CancellationTokenSource(timeout);
            while (Interlocked.Read(ref _pending) > 0 && !cts.IsCancellationRequested)
            {
                await Task.Delay(50, cts.Token);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        try
        {
            AppDomain.CurrentDomain.ProcessExit -= _processExit;
            _queue.Writer.TryComplete();
            if (LogDatabaseGate.IsOpen)
            {
                _worker.Wait(TimeSpan.FromSeconds(LogConstants.ShutdownFlushSeconds));
            }
            _stop.Cancel();
        }
        catch
        {
            // Shutdown flush is best effort.
        }
    }

    private static bool ShouldSkip(LogEvent logEvent)
    {
        if (logEvent.Properties.ContainsKey(LogConstants.SkipDbSinkProperty))
        {
            return true;
        }
        if (logEvent.Properties.TryGetValue("SourceContext", out var context)
            && context is ScalarValue { Value: string source }
            && source.StartsWith(OwnNamespace, StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }

    private async Task RunAsync()
    {
        // Anything logged while this task runs is ignored by Emit.
        InsideSink.Value = true;
        try
        {
            await LogDatabaseGate.WhenOpenAsync(_stop.Token);

            var reader = _queue.Reader;
            while (await reader.WaitToReadAsync())
            {
                var batch = new List<LogEvent>(LogConstants.BatchSize);
                using var window = new CancellationTokenSource(TimeSpan.FromSeconds(LogConstants.FlushIntervalSeconds));
                while (batch.Count < LogConstants.BatchSize)
                {
                    if (reader.TryRead(out var next))
                    {
                        batch.Add(next);
                        continue;
                    }
                    try
                    {
                        if (!await reader.WaitToReadAsync(window.Token))
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                if (batch.Count > 0)
                {
                    await WriteBatchAsync(batch);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped before the table became ready.
        }
        catch (Exception ex)
        {
            Report("Database log sink stopped: " + ex.Message, ex);
        }
    }

    private async Task WriteBatchAsync(List<LogEvent> batch)
    {
        try
        {
            var rows = new List<object>(batch.Count);
            foreach (var logEvent in batch)
            {
                rows.Add(ToRow(logEvent));
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction(deferred: false);
            await connection.ExecuteAsync(InsertSql, rows, transaction);
            await transaction.CommitAsync();
            _failing = false;
        }
        catch (Exception ex)
        {
            if (!_failing)
            {
                _failing = true;
                Report("Database log sink failed: " + ex.Message, ex);
            }
        }
        finally
        {
            Interlocked.Add(ref _pending, -batch.Count);
        }

        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            Report($"Database log sink dropped {dropped} events because its queue was full", null);
        }
    }

    private void Report(string message, Exception? error)
    {
        try
        {
            _reportFailure(message, error);
        }
        catch
        {
            // Reporting must never break the sink.
        }
    }

    private static object ToRow(LogEvent logEvent)
    {
        var category = LogConstants.CategoryApp;
        if (logEvent.Properties.TryGetValue(LogConstants.CategoryProperty, out var categoryValue)
            && categoryValue is ScalarValue { Value: string categoryText }
            && string.Equals(categoryText, LogConstants.CategoryEmail, StringComparison.Ordinal))
        {
            category = LogConstants.CategoryEmail;
        }

        string? correlationId = null;
        if (logEvent.Properties.TryGetValue(LogConstants.CorrelationIdProperty, out var correlationValue))
        {
            correlationId = correlationValue is ScalarValue { Value: { } raw }
                ? Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)
                : correlationValue.ToString().Trim('"');
        }

        return new
        {
            TimestampUtc = logEvent.Timestamp.UtcDateTime.ToString(TimestampFormat, System.Globalization.CultureInfo.InvariantCulture),
            Level = logEvent.Level.ToString(),
            Category = category,
            Message = Truncate(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture), MaxMessageLength),
            MessageTemplate = logEvent.MessageTemplate.Text,
            Exception = logEvent.Exception is null ? null : Truncate(logEvent.Exception.ToString(), MaxExceptionLength),
            CorrelationId = correlationId,
            PropertiesJson = BuildProperties(logEvent)
        };
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string BuildProperties(LogEvent logEvent)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in logEvent.Properties)
            {
                if (name is LogConstants.CorrelationIdProperty or LogConstants.CategoryProperty or LogConstants.SkipDbSinkProperty)
                {
                    continue;
                }
                writer.WritePropertyName(name);
                WriteValue(writer, value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, LogEventPropertyValue value)
    {
        if (value is not ScalarValue scalar)
        {
            writer.WriteStringValue(value.ToString());
            return;
        }

        switch (scalar.Value)
        {
            case null: writer.WriteNullValue(); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case byte or sbyte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(scalar.Value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case ulong ul: writer.WriteNumberValue(ul); break;
            case float f when float.IsFinite(f): writer.WriteNumberValue(f); break;
            case double d when double.IsFinite(d): writer.WriteNumberValue(d); break;
            case decimal m: writer.WriteNumberValue(m); break;
            default:
                writer.WriteStringValue(Convert.ToString(scalar.Value, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }
}
