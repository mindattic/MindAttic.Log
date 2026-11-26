using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MindAttic.Log.Migration;
using MindAttic.Log.Schema;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

/// <summary>
/// Exercises <see cref="MindAtticLogMigrator.MigrateSqliteToSqlServerAsync"/> against a throwaway
/// LocalDB database the migrator itself creates and this fixture drops, never a real app's database.
/// `[Explicit]` like <see cref="MindAtticLogMigratorSqlServerTests"/>: it needs
/// `(localdb)\MSSQLLocalDB` installed.
/// </summary>
[Explicit("Requires (localdb)\\MSSQLLocalDB; the migrator creates a throwaway database this fixture drops.")]
public class MindAtticLogMigratorSqliteToSqlServerTests
{
    private const string MasterConnectionString = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;";

    private string databaseName = null!;
    private string connectionString = null!;
    private string sourceDbPath = null!;

    [SetUp]
    public void SetUp()
    {
        databaseName = $"mindattic_log_sqlite_to_sql_test_{Guid.NewGuid():N}";
        connectionString = $@"Server=(localdb)\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;";
        sourceDbPath = Path.Combine(Path.GetTempPath(), $"mindattic-log-migrate-to-sql-source-{Guid.NewGuid()}.db");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString());
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = LogSchema.CreateTableSqlite;
        create.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        SqlConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(sourceDbPath + suffix)) File.Delete(sourceDbPath + suffix);

        try
        {
            using var master = new SqlConnection(MasterConnectionString);
            master.Open();
            using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID('{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
            drop.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort cleanup of a throwaway database; never fail the test over it.
        }
    }

    private void SeedSourceRow(DateTime timestampUtc, string message, string application = "SourceApp")
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString());
        connection.Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = $"""
            INSERT INTO {LogSchema.TableName} (TimestampUtc, Level, Application, Message, Category)
            VALUES ($timestampUtc, 2, $application, $message, NULL);
            """;
        insert.Parameters.AddWithValue("$timestampUtc", timestampUtc.ToString("o"));
        insert.Parameters.AddWithValue("$application", application);
        insert.Parameters.AddWithValue("$message", message);
        insert.ExecuteNonQuery();
    }

    private List<(string Application, string Message)> ReadDestinationRows()
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var select = connection.CreateCommand();
        select.CommandText = $"SELECT Application, Message FROM dbo.{LogSchema.TableName} ORDER BY TimestampUtc;";
        using var reader = select.ExecuteReader();
        var rows = new List<(string, string)>();
        while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    private string HashSource()
    {
        SqliteConnection.ClearAllPools();
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceDbPath)));
    }

    [Test]
    public async Task Copies_Every_Row_Into_A_Created_Database_And_Leaves_The_Source_File_Unchanged()
    {
        var start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        SeedSourceRow(start, "first");
        SeedSourceRow(start.AddSeconds(1), "second");
        SeedSourceRow(start.AddSeconds(2), "other", application: "OtherApp");
        var before = HashSource();

        var copied = await MindAtticLogMigrator.MigrateSqliteToSqlServerAsync(sourceDbPath, connectionString, batchSize: 2);

        Assert.That(copied, Is.EqualTo(3));
        Assert.That(ReadDestinationRows(), Is.EqualTo(new[] { ("SourceApp", "first"), ("SourceApp", "second"), ("OtherApp", "other") }));
        Assert.That(HashSource(), Is.EqualTo(before));
    }

    [Test]
    public async Task Rerun_Copies_Only_Rows_Newer_Than_The_Watermark()
    {
        var start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234);
        SeedSourceRow(start, "first");
        Assert.That(await MindAtticLogMigrator.MigrateSqliteToSqlServerAsync(sourceDbPath, connectionString), Is.EqualTo(1));

        SeedSourceRow(start.AddSeconds(1), "second");

        Assert.That(await MindAtticLogMigrator.MigrateSqliteToSqlServerAsync(sourceDbPath, connectionString), Is.EqualTo(1));
        Assert.That(await MindAtticLogMigrator.MigrateSqliteToSqlServerAsync(sourceDbPath, connectionString), Is.EqualTo(0));
        Assert.That(ReadDestinationRows().Select(r => r.Message), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public async Task Missing_Source_File_Is_A_No_Op()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"mindattic-log-missing-{Guid.NewGuid()}.db");

        Assert.That(await MindAtticLogMigrator.MigrateSqliteToSqlServerAsync(missing, connectionString), Is.EqualTo(0));
        Assert.That(File.Exists(missing), Is.False);
    }
}
