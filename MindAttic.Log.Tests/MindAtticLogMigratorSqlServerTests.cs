using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MindAttic.Log.Migration;
using MindAttic.Log.Schema;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

/// <summary>
/// Exercises <see cref="MindAtticLogMigrator.MigrateFromSqlServerAsync"/> against a real, throwaway
/// LocalDB database this fixture provisions and drops itself — never a real app's live database.
/// `[Explicit]`, same convention as this ecosystem's other live-SQL-Server tests (see
/// docs/MIGRATION.md's Ideas/Prose rows): it needs `(localdb)\MSSQLLocalDB` installed, so it's
/// skipped by a normal `dotnet test` run and exercised deliberately instead.
/// </summary>
[Explicit("Requires (localdb)\\MSSQLLocalDB; provisions and drops its own throwaway database.")]
public class MindAtticLogMigratorSqlServerTests
{
    private const string MasterConnectionString = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;";

    private string databaseName = null!;
    private string connectionString = null!;
    private string destDbPath = null!;

    [SetUp]
    public void SetUp()
    {
        databaseName = $"mindattic_log_migrator_test_{Guid.NewGuid():N}";
        connectionString = $@"Server=(localdb)\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;";
        destDbPath = Path.Combine(Path.GetTempPath(), $"mindattic-log-migrate-sqlserver-dest-{Guid.NewGuid()}.db");

        using var master = new SqlConnection(MasterConnectionString);
        master.Open();
        using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{databaseName}];";
        create.ExecuteNonQuery();

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var createTable = connection.CreateCommand();
        createTable.CommandText = LogSchema.CreateTableSqlServer;
        createTable.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(destDbPath)) File.Delete(destDbPath);
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(destDbPath + suffix)) File.Delete(destDbPath + suffix);

        try
        {
            using var master = new SqlConnection(MasterConnectionString);
            master.Open();
            using var drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];";
            drop.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort cleanup of a throwaway database; never fail the test over it.
        }
    }

    private void SeedSourceRow(DateTime timestampUtc, string message)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = $"""
            INSERT INTO dbo.{LogSchema.TableName} (TimestampUtc, Level, Application, Message)
            VALUES (@timestampUtc, 2, 'SourceApp', @message);
            """;
        insert.Parameters.AddWithValue("@timestampUtc", timestampUtc);
        insert.Parameters.AddWithValue("@message", message);
        insert.ExecuteNonQuery();
    }

    [Test]
    public async Task MigrateFromSqlServerAsync_Copies_Rows_Into_Destination_And_Drops_Source_When_Asked()
    {
        SeedSourceRow(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "first");
        SeedSourceRow(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "second");

        var copied = await MindAtticLogMigrator.MigrateFromSqlServerAsync(connectionString, destDbPath, dropSource: true);

        Assert.That(copied, Is.EqualTo(2));

        using var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destDbPath }.ToString());
        dest.Open();
        using var select = dest.CreateCommand();
        select.CommandText = $"SELECT Message FROM {LogSchema.TableName} ORDER BY TimestampUtc;";
        using var reader = select.ExecuteReader();
        var messages = new List<string>();
        while (reader.Read()) messages.Add(reader.GetString(0));
        Assert.That(messages, Is.EqualTo(new[] { "first", "second" }));

        using var source = new SqlConnection(connectionString);
        source.Open();
        using var checkTable = source.CreateCommand();
        checkTable.CommandText = $"SELECT COUNT(*) FROM sys.tables WHERE name = '{LogSchema.TableName}';";
        Assert.That(Convert.ToInt32(checkTable.ExecuteScalar()), Is.EqualTo(0), "dropSource: true should have dropped the source table.");
    }

    [Test]
    public async Task MigrateFromSqlServerAsync_After_The_Table_Was_Already_Dropped_Is_A_Clean_NoOp()
    {
        // Steady state every later app startup hits once a prior run already dropped the table.
        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE dbo.{LogSchema.TableName};";
            drop.ExecuteNonQuery();
        }

        var copied = await MindAtticLogMigrator.MigrateFromSqlServerAsync(connectionString, destDbPath, dropSource: true);

        Assert.That(copied, Is.EqualTo(0));
    }
}
