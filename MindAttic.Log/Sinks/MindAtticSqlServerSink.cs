using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using MindAttic.Log.Schema;
using Serilog.Core;
using Serilog.Events;

namespace MindAttic.Log.Sinks;

/// <summary>
/// Writes log events into the shared <c>MindAttic.Log</c> SQL Server database (M.LOGS-T17) using the
/// same <see cref="LogSchema"/> as <see cref="MindAtticSqliteSink"/>. Many apps share the one
/// database, told apart by the <c>Application</c> column, so retention only ever deletes this app's
/// own rows.
/// <para>
/// The database and table are created on first use, not in the constructor: a server that is down
/// or still starting when the host boots must not fail the host (M.LOGS-LAW-3). Until the schema is
/// in place each flush retries it, and a batch that can't be written is dropped, the same tradeoff
/// the SQLite sink makes.
/// </para>
/// </summary>
public sealed class MindAtticSqlServerSink : ILogEventSink, IDisposable
{
    /// <summary>The database used when the connection string names none.</summary>
    public const string DefaultDatabase = "MindAttic.Log";

    private readonly string connectionString;
    private readonly string application;
    private readonly string machineName;
    private readonly int retainedDays;
    private readonly ConcurrentQueue<LogEvent> pending = new();
    private readonly Timer flushTimer;
    private readonly Timer? retentionTimer;
    private readonly int batchSize;
    private readonly object flushLock = new();
    private int pendingCount;
    private bool schemaReady;

    public MindAtticSqlServerSink(string connectionString, string application, int batchSize = 200,
        TimeSpan? flushInterval = null, int retainedDays = 365, TimeSpan? retentionInterval = null)
    {
        this.connectionString = WithDefaultDatabase(connectionString);
        this.application = application;
        this.batchSize = batchSize;
        this.retainedDays = retainedDays;
        machineName = Environment.MachineName;

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

    /// <summary>Creates the database (through <c>master</c>) and the <see cref="LogSchema.TableName"/>
    /// table when either is missing. Internal so tests can run it without waiting on a flush.</summary>
    internal void EnsureSchema()
    {
        if (schemaReady) return;
        CreateSchema(connectionString);
        schemaReady = true;
    }

    /// <summary>The connection string with <see cref="DefaultDatabase"/> filled in when it names no
    /// database.</summary>
    internal static string WithDefaultDatabase(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)) builder.InitialCatalog = DefaultDatabase;
        return builder.ConnectionString;
    }

    /// <summary>Creates the connection string's database and its table when either is missing.</summary>
    internal static void CreateSchema(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        using (var master = new SqlConnection(builder.ConnectionString))
        {
            master.Open();
            using var create = master.CreateCommand();
            create.CommandText = $"IF DB_ID(@database) IS NULL CREATE DATABASE [{database.Replace("]", "]]")}];";
            create.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = database;
            create.ExecuteNonQuery();
        }

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var table = connection.CreateCommand();
        table.CommandText = LogSchema.CreateTableSqlServer;
        table.ExecuteNonQuery();
    }

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
                EnsureSchema();
                using var connection = new SqlConnection(connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO dbo.{LogSchema.TableName}
                        (TimestampUtc, Level, Application, Category, Message, MessageTemplate, Exception, PropertiesJson, CorrelationId, MachineName)
                    VALUES
                        (@timestampUtc, @level, @application, @category, @message, @messageTemplate, @exception, @propertiesJson, @correlationId, @machineName);
                    """;
                var timestampUtc = command.Parameters.Add("@timestampUtc", SqlDbType.DateTime2);
                timestampUtc.Scale = 3;
                var level = command.Parameters.Add("@level", SqlDbType.TinyInt);
                var applicationParam = command.Parameters.Add("@application", SqlDbType.NVarChar, 200);
                var category = command.Parameters.Add("@category", SqlDbType.NVarChar, 400);
                var message = command.Parameters.Add("@message", SqlDbType.NVarChar, -1);
                var messageTemplate = command.Parameters.Add("@messageTemplate", SqlDbType.NVarChar, -1);
                var exception = command.Parameters.Add("@exception", SqlDbType.NVarChar, -1);
                var propertiesJson = command.Parameters.Add("@propertiesJson", SqlDbType.NVarChar, -1);
                var correlationId = command.Parameters.Add("@correlationId", SqlDbType.NVarChar, 100);
                var machineNameParam = command.Parameters.Add("@machineName", SqlDbType.NVarChar, 200);

                foreach (var evt in batch)
                {
                    timestampUtc.Value = evt.Timestamp.UtcDateTime;
                    level.Value = (byte)LogEventFields.MapLevel(evt.Level);
                    applicationParam.Value = application;
                    category.Value = (object?)LogEventFields.SourceContextOf(evt) ?? DBNull.Value;
                    message.Value = evt.RenderMessage();
                    messageTemplate.Value = evt.MessageTemplate.Text;
                    exception.Value = (object?)evt.Exception?.ToString() ?? DBNull.Value;
                    propertiesJson.Value = (object?)LogEventFields.PropertiesOf(evt) ?? DBNull.Value;
                    correlationId.Value = (object?)LogEventFields.CorrelationIdOf(evt) ?? DBNull.Value;
                    machineNameParam.Value = machineName;
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception)
            {
                // A log write must never take the host app down (M.LOGS-LAW-3): an unreachable
                // server, a login failure or a full log file all drop this batch and nothing more.
            }
        }
    }

    /// <summary>Deletes this app's rows older than <see cref="retainedDays"/>; other apps' rows in the
    /// shared database are left to their own sinks. Never throws (M.LOGS-LAW-3). Internal so tests
    /// can invoke it deterministically instead of racing the timer.</summary>
    internal void PruneOldRows()
    {
        try
        {
            EnsureSchema();
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            using var delete = connection.CreateCommand();
            delete.CommandText = $"DELETE FROM dbo.{LogSchema.TableName} WHERE Application = @application AND TimestampUtc < @cutoff;";
            delete.Parameters.Add("@application", SqlDbType.NVarChar, 200).Value = application;
            var cutoff = delete.Parameters.Add("@cutoff", SqlDbType.DateTime2);
            cutoff.Scale = 3;
            cutoff.Value = DateTime.UtcNow.AddDays(-retainedDays);
            delete.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Best-effort housekeeping, same discipline as Flush() above.
        }
    }

    public void Dispose()
    {
        flushTimer.Dispose();
        retentionTimer?.Dispose();
        Flush();
    }
}
