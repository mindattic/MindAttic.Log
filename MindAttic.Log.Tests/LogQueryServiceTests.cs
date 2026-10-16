using System.Globalization;
using Microsoft.Data.Sqlite;
using MindAttic.Log.Models;
using MindAttic.Log.Reader.Services;
using MindAttic.Log.Schema;
using MindAttic.Log.Sinks;
using NUnit.Framework;
using Serilog.Events;
using Serilog.Parsing;

namespace MindAttic.Log.Tests;

public class LogQueryServiceTests
{
    private string dbPath = null!;
    private LogSource source = null!;
    private readonly LogQueryService service = new();

    [SetUp]
    public void SetUp()
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"mindattic-log-query-tests-{Guid.NewGuid()}.db");
        source = new LogSource { AppId = "MindAttic.Log.Tests", Path = dbPath };

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = LogSchema.CreateTableSqlite;
        create.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        // Same pooling caveat as MindAtticSqliteSinkTests: clear the pool before deleting the file.
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix);
    }

    private void InsertRow(DateTime timestampUtc, LogSeverity level = LogSeverity.Information,
        string application = "AppA", string? category = "MindAttic.AppA.Widget", string message = "hello",
        string? messageTemplate = null, string? exception = null, string? propertiesJson = null,
        string? correlationId = null, string? machineName = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = $"""
            INSERT INTO {LogSchema.TableName}
                (TimestampUtc, Level, Application, Category, Message, MessageTemplate, Exception, PropertiesJson, CorrelationId, MachineName)
            VALUES
                ($timestampUtc, $level, $application, $category, $message, $messageTemplate, $exception, $propertiesJson, $correlationId, $machineName);
            """;
        insert.Parameters.AddWithValue("$timestampUtc", timestampUtc.ToString("o"));
        insert.Parameters.AddWithValue("$level", (int)level);
        insert.Parameters.AddWithValue("$application", application);
        insert.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
        insert.Parameters.AddWithValue("$message", message);
        insert.Parameters.AddWithValue("$messageTemplate", (object?)messageTemplate ?? DBNull.Value);
        insert.Parameters.AddWithValue("$exception", (object?)exception ?? DBNull.Value);
        insert.Parameters.AddWithValue("$propertiesJson", (object?)propertiesJson ?? DBNull.Value);
        insert.Parameters.AddWithValue("$correlationId", (object?)correlationId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$machineName", (object?)machineName ?? DBNull.Value);
        insert.ExecuteNonQuery();
    }

    [Test]
    public async Task QueryAsync_With_Empty_Filter_Returns_Everything()
    {
        InsertRow(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        InsertRow(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var results = await service.QueryAsync(source, new LogFilter());

        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task QueryAsync_SinceUtc_Is_Inclusive()
    {
        var boundary = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        InsertRow(boundary.AddSeconds(-1), message: "before");
        InsertRow(boundary, message: "on-boundary");
        InsertRow(boundary.AddSeconds(1), message: "after");

        var results = await service.QueryAsync(source, new LogFilter { SinceUtc = boundary });

        Assert.That(results.Select(e => e.Message), Is.EquivalentTo(new[] { "on-boundary", "after" }));
    }

    [Test]
    public async Task QueryAsync_UntilUtc_Is_Inclusive()
    {
        var boundary = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        InsertRow(boundary.AddSeconds(-1), message: "before");
        InsertRow(boundary, message: "on-boundary");
        InsertRow(boundary.AddSeconds(1), message: "after");

        var results = await service.QueryAsync(source, new LogFilter { UntilUtc = boundary });

        Assert.That(results.Select(e => e.Message), Is.EquivalentTo(new[] { "before", "on-boundary" }));
    }

    [Test]
    public async Task QueryAsync_MinLevel_Keeps_Level_At_Or_Above()
    {
        InsertRow(DateTime.UtcNow, level: LogSeverity.Verbose, message: "verbose");
        InsertRow(DateTime.UtcNow, level: LogSeverity.Debug, message: "debug");
        InsertRow(DateTime.UtcNow, level: LogSeverity.Warning, message: "warning");
        InsertRow(DateTime.UtcNow, level: LogSeverity.Error, message: "error");

        var results = await service.QueryAsync(source, new LogFilter { MinLevel = (int)LogSeverity.Warning });

        Assert.That(results.Select(e => e.Message), Is.EquivalentTo(new[] { "warning", "error" }));
    }

    [Test]
    public async Task QueryAsync_Application_Is_Exact_Match()
    {
        InsertRow(DateTime.UtcNow, application: "AppA", message: "a");
        InsertRow(DateTime.UtcNow, application: "AppAB", message: "ab");
        InsertRow(DateTime.UtcNow, application: "AppB", message: "b");

        var results = await service.QueryAsync(source, new LogFilter { Application = "AppA" });

        Assert.That(results.Select(e => e.Message), Is.EquivalentTo(new[] { "a" }));
    }

    [Test]
    public async Task QueryAsync_Category_Is_Case_Insensitive_Substring_Match()
    {
        InsertRow(DateTime.UtcNow, category: "MindAttic.AppA.Widget", message: "widget");
        InsertRow(DateTime.UtcNow, category: "MindAttic.AppA.Gadget", message: "gadget");
        InsertRow(DateTime.UtcNow, category: null, message: "no-category");

        var results = await service.QueryAsync(source, new LogFilter { Category = "WIDGET" });

        Assert.That(results.Select(e => e.Message), Is.EquivalentTo(new[] { "widget" }));
    }

    [Test]
    public async Task QueryAsync_SearchText_Is_Case_Insensitive_Substring_Match()
    {
        InsertRow(DateTime.UtcNow, message: "Connection Failed");
        InsertRow(DateTime.UtcNow, message: "Everything is fine");

        var results = await service.QueryAsync(source, new LogFilter { SearchText = "failed" });

        Assert.That(results.Select(e => e.Message), Is.EquivalentTo(new[] { "Connection Failed" }));
    }

    [Test]
    public async Task QueryAsync_Combines_Filters_With_And()
    {
        InsertRow(DateTime.UtcNow, application: "AppA", level: LogSeverity.Error, message: "match");
        InsertRow(DateTime.UtcNow, application: "AppA", level: LogSeverity.Information, message: "wrong-level");
        InsertRow(DateTime.UtcNow, application: "AppB", level: LogSeverity.Error, message: "wrong-app");

        var results = await service.QueryAsync(source, new LogFilter { Application = "AppA", MinLevel = (int)LogSeverity.Error });

        Assert.That(results.Select(e => e.Message), Is.EquivalentTo(new[] { "match" }));
    }

    [Test]
    public async Task QueryAsync_Orders_Newest_First()
    {
        InsertRow(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), message: "oldest");
        InsertRow(new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), message: "newest");
        InsertRow(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), message: "middle");

        var results = await service.QueryAsync(source, new LogFilter());

        Assert.That(results.Select(e => e.Message), Is.EqualTo(new[] { "newest", "middle", "oldest" }));
    }

    [Test]
    public async Task QueryAsync_Caps_Results_At_MaxResults()
    {
        for (var i = 0; i < 5; i++) InsertRow(DateTime.UtcNow.AddSeconds(i), message: $"row-{i}");

        var results = await service.QueryAsync(source, new LogFilter { MaxResults = 3 });

        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task QueryAsync_Returns_Empty_And_Creates_Nothing_When_File_Is_Missing()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(dbPath);

        var results = await service.QueryAsync(source, new LogFilter());

        Assert.That(results, Is.Empty);
        Assert.That(File.Exists(dbPath), Is.False);
    }

    [Test]
    public async Task QueryAsync_Returns_Empty_For_A_Non_Sqlite_File()
    {
        SqliteConnection.ClearAllPools();
        File.WriteAllBytes(dbPath, "not a sqlite database"u8.ToArray());

        var results = await service.QueryAsync(source, new LogFilter());

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_Returns_Empty_When_The_Log_Table_Is_Missing()
    {
        var tablelessPath = Path.Combine(Path.GetTempPath(), $"mindattic-log-query-tests-tableless-{Guid.NewGuid()}.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tablelessPath }.ToString()))
            {
                connection.Open();
                using var create = connection.CreateCommand();
                create.CommandText = "CREATE TABLE SomeOtherTable (Id INTEGER PRIMARY KEY);";
                create.ExecuteNonQuery();
            }

            var tablelessSource = new LogSource { AppId = "Tableless", Path = tablelessPath };
            var results = await service.QueryAsync(tablelessSource, new LogFilter());

            Assert.That(results, Is.Empty);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tablelessPath)) File.Delete(tablelessPath);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(tablelessPath + suffix)) File.Delete(tablelessPath + suffix);
        }
    }

    [Test]
    public async Task QueryAsync_Opens_The_Database_ReadOnly_And_Never_Writes_To_It()
    {
        InsertRow(DateTime.UtcNow);
        SqliteConnection.ClearAllPools();
        var bytesBefore = File.ReadAllBytes(dbPath);

        await service.QueryAsync(source, new LogFilter());

        SqliteConnection.ClearAllPools();
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(dbPath), Is.EqualTo(bytesBefore));
            Assert.That(File.Exists(dbPath + "-wal"), Is.False);
            Assert.That(File.Exists(dbPath + "-shm"), Is.False);
        });
    }

    [Test]
    public async Task QueryAsync_Can_Read_While_A_Sink_Holds_The_Database_Open_In_WalMode()
    {
        using var sink = new MindAtticSqliteSink(dbPath, application: "AppA", batchSize: 1);
        var template = new MessageTemplateParser().Parse("Sink is live");
        sink.Emit(new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, template, []));

        var results = await service.QueryAsync(source, new LogFilter());

        Assert.That(results.Select(e => e.Message), Is.EquivalentTo(new[] { "Sink is live" }));
    }

    [Test]
    public async Task ReadEntry_Round_Trips_Every_Column_When_Populated()
    {
        var timestamp = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        InsertRow(timestamp, level: LogSeverity.Error, application: "AppA", category: "MindAttic.AppA.Widget",
            message: "boom", messageTemplate: "boom {X}", exception: "System.Exception: boom",
            propertiesJson: """{"X":1}""", correlationId: "corr-1", machineName: "MACHINE1");

        var results = await service.QueryAsync(source, new LogFilter());

        Assert.That(results, Has.Count.EqualTo(1));
        var entry = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.Id, Is.GreaterThan(0));
            Assert.That(entry.TimestampUtc, Is.EqualTo(timestamp));
            Assert.That(entry.TimestampUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(entry.Level, Is.EqualTo(LogSeverity.Error));
            Assert.That(entry.Application, Is.EqualTo("AppA"));
            Assert.That(entry.Category, Is.EqualTo("MindAttic.AppA.Widget"));
            Assert.That(entry.Message, Is.EqualTo("boom"));
            Assert.That(entry.MessageTemplate, Is.EqualTo("boom {X}"));
            Assert.That(entry.Exception, Is.EqualTo("System.Exception: boom"));
            Assert.That(entry.PropertiesJson, Is.EqualTo("""{"X":1}"""));
            Assert.That(entry.CorrelationId, Is.EqualTo("corr-1"));
            Assert.That(entry.MachineName, Is.EqualTo("MACHINE1"));
        });
    }

    [Test]
    public async Task ReadEntry_Round_Trips_Nullable_Columns_As_Null()
    {
        InsertRow(DateTime.UtcNow, category: null);

        var results = await service.QueryAsync(source, new LogFilter());

        Assert.That(results, Has.Count.EqualTo(1));
        var entry = results[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.Category, Is.Null);
            Assert.That(entry.MessageTemplate, Is.Null);
            Assert.That(entry.Exception, Is.Null);
            Assert.That(entry.PropertiesJson, Is.Null);
            Assert.That(entry.CorrelationId, Is.Null);
            Assert.That(entry.MachineName, Is.Null);
        });
    }

    [Test]
    public async Task ReadEntry_Parses_TimestampUtc_As_Utc_Regardless_Of_Current_Culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // Thai culture defaults to the Buddhist calendar, the classic trap for
            // culture-sensitive DateTime parsing: a careless parse shifts the year by 543.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            var timestamp = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);
            InsertRow(timestamp);

            var results = await service.QueryAsync(source, new LogFilter());

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(results[0].TimestampUtc, Is.EqualTo(timestamp));
                Assert.That(results[0].TimestampUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
