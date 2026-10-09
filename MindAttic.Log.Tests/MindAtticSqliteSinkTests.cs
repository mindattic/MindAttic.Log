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
}
