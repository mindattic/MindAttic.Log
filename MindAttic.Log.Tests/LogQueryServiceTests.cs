using Microsoft.Data.Sqlite;
using MindAttic.Log.Models;
using MindAttic.Log.Reader.Services;
using MindAttic.Log.Schema;
using NUnit.Framework;

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
        string application = "AppA", string? category = "MindAttic.AppA.Widget", string message = "hello")
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = $"""
            INSERT INTO {LogSchema.TableName} (TimestampUtc, Level, Application, Category, Message)
            VALUES ($timestampUtc, $level, $application, $category, $message);
            """;
        insert.Parameters.AddWithValue("$timestampUtc", timestampUtc.ToString("o"));
        insert.Parameters.AddWithValue("$level", (int)level);
        insert.Parameters.AddWithValue("$application", application);
        insert.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
        insert.Parameters.AddWithValue("$message", message);
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
}
