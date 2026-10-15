using Microsoft.Data.Sqlite;
using MindAttic.Log.Schema;
using MindAttic.Log.Sinks;
using NUnit.Framework;
using Serilog.Events;
using Serilog.Parsing;

namespace MindAttic.Log.Tests;

public class MindAtticSqliteSinkTests
{
    private string dbPath = null!;

    [SetUp]
    public void SetUp() => dbPath = Path.Combine(Path.GetTempPath(), $"mindattic-log-sink-tests-{Guid.NewGuid()}.db");

    [TearDown]
    public void TearDown()
    {
        // Microsoft.Data.Sqlite pools native connections by default, which keeps a file handle
        // open past the managed SqliteConnection's Dispose — clear the pool first or deleting
        // the just-closed db file races the pool's own cleanup.
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix);
    }

    [Test]
    public void Emit_Then_Dispose_Flushes_A_Readable_Row()
    {
        using (var sink = new MindAtticSqliteSink(dbPath, application: "MindAttic.Log.Tests"))
        {
            var template = new MessageTemplateParser().Parse("Hello {Name}");
            var properties = new[] { new LogEventProperty("Name", new ScalarValue("World")) };
            var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Warning, null, template, properties);
            sink.Emit(logEvent);
        } // Dispose() forces a flush

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Level, Application, Message, MessageTemplate, PropertiesJson FROM {LogSchema.TableName};";
        using var reader = command.ExecuteReader();

        Assert.That(reader.Read(), Is.True, "Expected one row after flush.");
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo((int)Models.LogSeverity.Warning));
            Assert.That(reader.GetString(1), Is.EqualTo("MindAttic.Log.Tests"));
            Assert.That(reader.GetString(2), Is.EqualTo("Hello \"World\""));
            Assert.That(reader.GetString(3), Is.EqualTo("Hello {Name}"));
            Assert.That(reader.GetString(4), Does.Contain("World"));
        });
    }

    [Test]
    public void Constructor_Creates_Table_Even_With_No_Events_Emitted()
    {
        using (new MindAtticSqliteSink(dbPath, application: "MindAttic.Log.Tests")) { }
        Assert.That(File.Exists(dbPath), Is.True);
    }

    [Test]
    public void PruneOldRows_Deletes_Rows_Older_Than_RetainedDays_But_Keeps_Newer_Ones()
    {
        using var sink = new MindAtticSqliteSink(dbPath, application: "MindAttic.Log.Tests", retainedDays: 30);

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
        {
            connection.Open();
            InsertRow(connection, DateTime.UtcNow.AddDays(-60), "too old");
            InsertRow(connection, DateTime.UtcNow.AddDays(-1), "recent");
        }

        sink.PruneOldRows();

        using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        verify.Open();
        using var command = verify.CreateCommand();
        command.CommandText = $"SELECT Message FROM {LogSchema.TableName};";
        using var reader = command.ExecuteReader();
        var messages = new List<string>();
        while (reader.Read()) messages.Add(reader.GetString(0));

        Assert.That(messages, Is.EqualTo(new[] { "recent" }));
    }

    private static void InsertRow(SqliteConnection connection, DateTime timestampUtc, string message)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = $"""
            INSERT INTO {LogSchema.TableName} (TimestampUtc, Level, Application, Message)
            VALUES ($timestampUtc, 2, 'MindAttic.Log.Tests', $message);
            """;
        insert.Parameters.AddWithValue("$timestampUtc", timestampUtc.ToString("o"));
        insert.Parameters.AddWithValue("$message", message);
        insert.ExecuteNonQuery();
    }
}
