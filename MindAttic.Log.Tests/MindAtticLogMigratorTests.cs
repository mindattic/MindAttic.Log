using Microsoft.Data.Sqlite;
using MindAttic.Log.Migration;
using MindAttic.Log.Schema;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

public class MindAtticLogMigratorTests
{
    private string sourceDbPath = null!;
    private string destDbPath = null!;

    [SetUp]
    public void SetUp()
    {
        sourceDbPath = Path.Combine(Path.GetTempPath(), $"mindattic-log-migrate-source-{Guid.NewGuid()}.db");
        destDbPath = Path.Combine(Path.GetTempPath(), $"mindattic-log-migrate-dest-{Guid.NewGuid()}.db");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { sourceDbPath, destDbPath })
        {
            if (File.Exists(path)) File.Delete(path);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private void SeedSource(params (DateTime TimestampUtc, string Message)[] rows)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString());
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = LogSchema.CreateTableSqlite;
            create.ExecuteNonQuery();
        }
        foreach (var (timestampUtc, message) in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = $"""
                INSERT INTO {LogSchema.TableName} (TimestampUtc, Level, Application, Message)
                VALUES ($timestampUtc, 2, 'SourceApp', $message);
                """;
            insert.Parameters.AddWithValue("$timestampUtc", timestampUtc.ToString("o"));
            insert.Parameters.AddWithValue("$message", message);
            insert.ExecuteNonQuery();
        }
    }

    private List<string> ReadDestMessages()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destDbPath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Message FROM {LogSchema.TableName} ORDER BY TimestampUtc;";
        using var reader = command.ExecuteReader();
        var messages = new List<string>();
        while (reader.Read()) messages.Add(reader.GetString(0));
        return messages;
    }

    [Test]
    public async Task MigrateFromSqliteAsync_Copies_All_Rows_Into_Destination()
    {
        SeedSource(
            (new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "first"),
            (new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "second"));

        var copied = await MindAtticLogMigrator.MigrateFromSqliteAsync(sourceDbPath, destDbPath);

        Assert.That(copied, Is.EqualTo(2));
        Assert.That(ReadDestMessages(), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public async Task MigrateFromSqliteAsync_Rerun_Does_Not_Duplicate_Already_Copied_Rows()
    {
        SeedSource(
            (new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "first"),
            (new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "second"));

        await MindAtticLogMigrator.MigrateFromSqliteAsync(sourceDbPath, destDbPath);
        var secondRunCopied = await MindAtticLogMigrator.MigrateFromSqliteAsync(sourceDbPath, destDbPath);

        Assert.That(secondRunCopied, Is.EqualTo(0));
        Assert.That(ReadDestMessages(), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public async Task MigrateFromSqliteAsync_With_DropSource_Removes_The_Source_Table()
    {
        SeedSource((new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "first"));

        await MindAtticLogMigrator.MigrateFromSqliteAsync(sourceDbPath, destDbPath, dropSource: true);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{LogSchema.TableName}';";
        Assert.That(Convert.ToInt32(command.ExecuteScalar()), Is.EqualTo(0));
    }

    [Test]
    public async Task MigrateFromSqliteAsync_Without_DropSource_Leaves_The_Source_Table_Intact()
    {
        SeedSource((new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "first"));

        await MindAtticLogMigrator.MigrateFromSqliteAsync(sourceDbPath, destDbPath, dropSource: false);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {LogSchema.TableName};";
        Assert.That(Convert.ToInt32(command.ExecuteScalar()), Is.EqualTo(1));
    }

    [Test]
    public async Task MigrateFromSqliteAsync_With_No_Source_Rows_Copies_Nothing()
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString()))
        {
            connection.Open();
            using var create = connection.CreateCommand();
            create.CommandText = LogSchema.CreateTableSqlite;
            create.ExecuteNonQuery();
        }

        var copied = await MindAtticLogMigrator.MigrateFromSqliteAsync(sourceDbPath, destDbPath);

        Assert.That(copied, Is.EqualTo(0));
    }

    [Test]
    public async Task MigrateFromSqliteAsync_With_Missing_Source_File_Is_A_Clean_NoOp()
    {
        // sourceDbPath was never created — covers both "this app has no database of its own" and
        // the steady state every later startup hits after a successful dropSource run elsewhere.
        var copied = await MindAtticLogMigrator.MigrateFromSqliteAsync(sourceDbPath, destDbPath);

        Assert.That(copied, Is.EqualTo(0));
        Assert.That(File.Exists(sourceDbPath), Is.False, "Must not create a stray file at the missing source path.");
    }

    [Test]
    public async Task MigrateFromSqliteAsync_With_Source_File_But_No_Table_Is_A_Clean_NoOp()
    {
        // The source file exists (e.g. an app's own SQLite database) but has never had a
        // MindAttic_Log table — a brand-new app, or steady state after a prior dropSource run.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString()))
        {
            connection.Open();
            using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE SomeOtherTable (Id INTEGER PRIMARY KEY);";
            create.ExecuteNonQuery();
        }

        var copied = await MindAtticLogMigrator.MigrateFromSqliteAsync(sourceDbPath, destDbPath);

        Assert.That(copied, Is.EqualTo(0));
    }
}
