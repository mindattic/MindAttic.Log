using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MindAttic.Log.Schema;
using Serilog.Core;
using Serilog.Events;

namespace MindAttic.Log.Sinks;

/// <summary>
/// Writes log events into a SQLite file using the shared <see cref="LogSchema"/>. Used for both
/// MindAttic.Log tiers that land on SQLite: an app's own database file (point <c>DbPath</c> at it
/// — the sink only ever touches its own table) and the no-database tier (point it at a rolled
/// <c>MindAttic.Log.&lt;yyyy-MM&gt;.db</c> file via <see cref="LogFileRoller"/>).
/// <para>
/// Batches on a background timer rather than one transaction per event — validated by prior art
/// (Blacklite/logdive: see docs/BIBLE.md §4) as the difference between SQLite keeping up with
/// write-heavy logging and falling behind it. WAL mode is turned on so the Log Reader can query
/// the file concurrently while the app keeps writing.
/// </para>
/// </summary>
public sealed class MindAtticSqliteSink : ILogEventSink, IDisposable
{
    private readonly string connectionString;
    private readonly string application;
    private readonly string machineName;
    private readonly ConcurrentQueue<LogEvent> pending = new();
    private readonly Timer flushTimer;
    private readonly int batchSize;
    private int pendingCount;

    public MindAtticSqliteSink(string dbPath, string application, int batchSize = 200, TimeSpan? flushInterval = null)
    {
        this.application = application;
        this.batchSize = batchSize;
        machineName = Environment.MachineName;

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
            using var create = connection.CreateCommand();
            create.CommandText = LogSchema.CreateTableSqlite;
            create.ExecuteNonQuery();
        }

        flushTimer = new Timer(_ => Flush(), null, flushInterval ?? TimeSpan.FromSeconds(2), flushInterval ?? TimeSpan.FromSeconds(2));
    }

    public void Emit(LogEvent logEvent)
    {
        pending.Enqueue(logEvent);
        if (Interlocked.Increment(ref pendingCount) >= batchSize) Flush();
    }

    private readonly object flushLock = new();

    private void Flush()
    {
        if (pending.IsEmpty) return;
        lock (flushLock)
        {
            var batch = new List<LogEvent>();
            while (pending.TryDequeue(out var evt))
            {
                batch.Add(evt);
                Interlocked.Decrement(ref pendingCount);
            }
            if (batch.Count == 0) return;

            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO {LogSchema.TableName}
                        (TimestampUtc, Level, Application, Category, Message, MessageTemplate, Exception, PropertiesJson, CorrelationId, MachineName)
                    VALUES
                        ($timestampUtc, $level, $application, $category, $message, $messageTemplate, $exception, $propertiesJson, $correlationId, $machineName);
                    """;
                var timestampUtc = command.CreateParameter(); timestampUtc.ParameterName = "$timestampUtc"; command.Parameters.Add(timestampUtc);
                var level = command.CreateParameter(); level.ParameterName = "$level"; command.Parameters.Add(level);
                var applicationParam = command.CreateParameter(); applicationParam.ParameterName = "$application"; command.Parameters.Add(applicationParam);
                var category = command.CreateParameter(); category.ParameterName = "$category"; command.Parameters.Add(category);
                var message = command.CreateParameter(); message.ParameterName = "$message"; command.Parameters.Add(message);
                var messageTemplate = command.CreateParameter(); messageTemplate.ParameterName = "$messageTemplate"; command.Parameters.Add(messageTemplate);
                var exception = command.CreateParameter(); exception.ParameterName = "$exception"; command.Parameters.Add(exception);
                var propertiesJson = command.CreateParameter(); propertiesJson.ParameterName = "$propertiesJson"; command.Parameters.Add(propertiesJson);
                var correlationId = command.CreateParameter(); correlationId.ParameterName = "$correlationId"; command.Parameters.Add(correlationId);
                var machineNameParam = command.CreateParameter(); machineNameParam.ParameterName = "$machineName"; command.Parameters.Add(machineNameParam);

                foreach (var evt in batch)
                {
                    timestampUtc.Value = evt.Timestamp.UtcDateTime.ToString("o");
                    level.Value = (int)MapLevel(evt.Level);
                    applicationParam.Value = application;
                    category.Value = (object?)SourceContextOf(evt) ?? DBNull.Value;
                    message.Value = evt.RenderMessage();
                    messageTemplate.Value = evt.MessageTemplate.Text;
                    exception.Value = (object?)evt.Exception?.ToString() ?? DBNull.Value;
                    propertiesJson.Value = (object?)PropertiesOf(evt) ?? DBNull.Value;
                    correlationId.Value = (object?)CorrelationIdOf(evt) ?? DBNull.Value;
                    machineNameParam.Value = machineName;
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (SqliteException)
            {
                // A log write must never take the host app down. Dropping a batch on a sink
                // failure is the right tradeoff — see docs/BIBLE.md §4.
            }
        }
    }

    private static Models.LogSeverity MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => Models.LogSeverity.Verbose,
        LogEventLevel.Debug => Models.LogSeverity.Debug,
        LogEventLevel.Information => Models.LogSeverity.Information,
        LogEventLevel.Warning => Models.LogSeverity.Warning,
        LogEventLevel.Error => Models.LogSeverity.Error,
        LogEventLevel.Fatal => Models.LogSeverity.Fatal,
        _ => Models.LogSeverity.Information,
    };

    private static string? SourceContextOf(LogEvent evt) =>
        evt.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string s } ? s : null;

    private static string? CorrelationIdOf(LogEvent evt) =>
        evt.Properties.TryGetValue("CorrelationId", out var value) && value is ScalarValue { Value: string s } ? s : null;

    private static string? PropertiesOf(LogEvent evt)
    {
        var extra = evt.Properties
            .Where(p => p.Key is not ("SourceContext" or "CorrelationId"))
            .ToDictionary(p => p.Key, p => p.Value.ToString());
        return extra.Count == 0 ? null : JsonSerializer.Serialize(extra);
    }

    public void Dispose()
    {
        flushTimer.Dispose();
        Flush();
    }
}
