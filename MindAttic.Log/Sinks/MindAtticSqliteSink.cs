using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MindAttic.Log.Schema;
using Serilog.Core;
using Serilog.Events;

namespace MindAttic.Log.Sinks;

/// <summary>
/// Writes log events into an app's standalone <c>MindAttic.Log.db</c> using the shared
/// <see cref="LogSchema"/> — the one remaining tier (see docs/BIBLE.md §4); the path is resolved by
/// the host via <c>VaultPaths.LoggingDbPath</c>, never computed by this sink itself.
/// <para>
/// Batches on a background timer rather than one transaction per event — validated by prior art
/// (Blacklite/logdive: see docs/BIBLE.md §4) as the difference between SQLite keeping up with
/// write-heavy logging and falling behind it. WAL mode is turned on so the Log Reader can query
/// the file concurrently while the app keeps writing. A second timer sweeps rows older than
/// <c>retainedDays</c> (see <see cref="PruneOldRows"/>) — the file is never rolled or replaced, so
/// row-based retention is what keeps it bounded.
/// </para>
/// </summary>
public sealed class MindAtticSqliteSink : ILogEventSink, IDisposable
{
    private readonly string connectionString;
    private readonly string application;
    private readonly string machineName;
    private readonly int retainedDays;
    private readonly ConcurrentQueue<LogEvent> pending = new();
    private readonly Timer flushTimer;
    private readonly Timer? retentionTimer;
    private readonly int batchSize;
    private int pendingCount;

    public MindAtticSqliteSink(string dbPath, string application, int batchSize = 200,
        TimeSpan? flushInterval = null, int retainedDays = 365, TimeSpan? retentionInterval = null)
    {
        this.application = application;
        this.batchSize = batchSize;
        this.retainedDays = retainedDays;
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
        if (retainedDays > 0)
        {
            var interval = retentionInterval ?? TimeSpan.FromHours(24);
            retentionTimer = new Timer(_ => PruneOldRows(), null, interval, interval);
        }
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
            catch (Exception)
            {
                // A log write must never take the host app down (M.LOGS-LAW-3) — this runs on a bare
                // System.Threading.Timer callback with no other safety net, so an uncaught
                // exception here (not just SqliteException: a full disk can surface as IOException,
                // a locked file as UnauthorizedAccessException) would otherwise crash the whole
                // host process. Dropping a batch on any sink failure is the right tradeoff.
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

    /// <summary>
    /// Row-based retention — replaces the old no-database tier's file-count rolling now that every
    /// app writes one standalone, never-rolled <c>MindAttic.Log.db</c> (see docs/BIBLE.md §4):
    /// delete rows older than <see cref="retainedDays"/> and reclaim the freed space. Runs on its
    /// own timer, independent of the flush batch, and never throws (M.LOGS-LAW-3) — a full disk or a
    /// locked file here must not take the host app down any more than a failed write would.
    /// Internal (not private) so tests can invoke it deterministically instead of racing the timer.
    /// </summary>
    internal void PruneOldRows()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-retainedDays).ToString("o");
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using (var delete = connection.CreateCommand())
            {
                delete.CommandText = $"DELETE FROM {LogSchema.TableName} WHERE TimestampUtc < $cutoff;";
                var cutoffParam = delete.CreateParameter();
                cutoffParam.ParameterName = "$cutoff";
                cutoffParam.Value = cutoff;
                delete.Parameters.Add(cutoffParam);
                delete.ExecuteNonQuery();
            }
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM;";
            vacuum.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Best-effort housekeeping, same discipline as Flush() above — see M.LOGS-LAW-3.
        }
    }

    public void Dispose()
    {
        flushTimer.Dispose();
        retentionTimer?.Dispose();
        Flush();
    }
}
