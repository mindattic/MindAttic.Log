using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MindAttic.Log.Schema;

namespace MindAttic.Log.Migration;

/// <summary>
/// One-shot, host-invoked copy of historical <c>MindAttic_Log</c> rows out of the retired SQL
/// Server / app-owned-SQLite tiers and into an app's new standalone <c>MindAttic.Log.db</c> (see
/// docs/MIGRATION.md "Phase 2"). Never runs automatically and never resolves its own source
/// connection info (LOG-LAW-2) — the caller already has it, since it's what <c>AddMindAtticLog</c>
/// used to be pointed at before that app's Phase-2 pass.
/// <para>
/// Idempotent by watermark: re-running after a partial failure only copies rows newer than the
/// newest <c>TimestampUtc</c> already present in the destination for that <c>Application</c>,
/// rather than re-inserting everything. This is a one-shot historical copy, not an ongoing sync —
/// rows that happen to share the exact watermark timestamp are not re-copied, which is an
/// acceptable gap for a never-repeated migration, not a general dedup guarantee.
/// </para>
/// <para>
/// Dropping the source table is opt-in (<c>dropSource: true</c>) and only runs after every row has
/// copied successfully — never implicit, matching this repo's own rule that a migration doesn't get
/// to alter a live store on its own authority (see docs/BIBLE.md §3).
/// </para>
/// </summary>
public static class MindAtticLogMigrator
{
    public static async Task<int> MigrateFromSqlServerAsync(
        string sourceConnectionString, string destDbPath, bool dropSource = false, CancellationToken cancel = default)
    {
        await using var source = new SqlConnection(sourceConnectionString);
        await source.OpenAsync(cancel);

        // Steady state after a successful dropSource run (or a source that never had the table):
        // every later app startup calls this again, so "no table" must be a clean no-op, not a
        // thrown exception that would otherwise take the host's startup path down with it.
        if (!await SqlServerTableExistsAsync(source, cancel)) return 0;

        var application = await ReadApplicationAsync(source, cancel);
        var watermark = ReadDestinationWatermark(destDbPath, application);

        await using var select = source.CreateCommand();
        select.CommandText = $"""
            SELECT TimestampUtc, Level, Application, Category, Message, MessageTemplate, Exception, PropertiesJson, CorrelationId, MachineName
            FROM dbo.{LogSchema.TableName}
            WHERE TimestampUtc > @watermark
            ORDER BY TimestampUtc;
            """;
        // BUG: ADO.NET infers an untyped DateTime parameter as the legacy SQL `datetime` type (range
        // 1753-9999), not the column's own `datetime2` — DateTime.MinValue (year 1) then overflows
        // *the parameter*, not the column, on the very first (watermark-less) run. Pin the parameter
        // type explicitly so it matches `LogSchema.CreateTableSqlServer`'s `datetime2(3)` column.
        var watermarkParam = (SqlParameter)select.CreateParameter();
        watermarkParam.ParameterName = "@watermark";
        watermarkParam.SqlDbType = System.Data.SqlDbType.DateTime2;
        watermarkParam.Value = watermark;
        select.Parameters.Add(watermarkParam);

        var copied = 0;
        await using var reader = await select.ExecuteReaderAsync(cancel);
        await using (var dest = OpenDestination(destDbPath))
        await using (var insert = CreateInsertCommand(dest))
        {
            while (await reader.ReadAsync(cancel))
            {
                BindRow(insert, reader);
                await insert.ExecuteNonQueryAsync(cancel);
                copied++;
            }
        }
        await reader.DisposeAsync();

        if (dropSource)
        {
            await using var drop = source.CreateCommand();
            drop.CommandText = $"DROP TABLE dbo.{LogSchema.TableName};";
            await drop.ExecuteNonQueryAsync(cancel);
        }

        return copied;
    }

    public static async Task<int> MigrateFromSqliteAsync(
        string sourceDbPath, string destDbPath, bool dropSource = false, CancellationToken cancel = default)
    {
        // A source file that doesn't exist at all (never had any logging, or this app has no
        // database of its own) has nothing to migrate — and opening a SqliteConnection pointed at
        // a missing path would otherwise create an empty stray file as a side effect.
        if (!File.Exists(sourceDbPath)) return 0;

        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceDbPath }.ToString());
        await source.OpenAsync(cancel);

        // Steady state after a successful dropSource run: every later app startup calls this
        // again, so "no table" must be a clean no-op, not a thrown exception.
        if (!await SqliteTableExistsAsync(source, cancel)) return 0;

        var application = await ReadApplicationAsync(source, cancel);
        var watermark = ReadDestinationWatermark(destDbPath, application);

        await using var select = source.CreateCommand();
        select.CommandText = $"""
            SELECT TimestampUtc, Level, Application, Category, Message, MessageTemplate, Exception, PropertiesJson, CorrelationId, MachineName
            FROM {LogSchema.TableName}
            WHERE TimestampUtc > $watermark
            ORDER BY TimestampUtc;
            """;
        var watermarkParam = select.CreateParameter();
        watermarkParam.ParameterName = "$watermark";
        watermarkParam.Value = watermark.ToString("o");
        select.Parameters.Add(watermarkParam);

        var copied = 0;
        await using var reader = await select.ExecuteReaderAsync(cancel);
        await using (var dest = OpenDestination(destDbPath))
        await using (var insert = CreateInsertCommand(dest))
        {
            while (await reader.ReadAsync(cancel))
            {
                BindRow(insert, reader);
                await insert.ExecuteNonQueryAsync(cancel);
                copied++;
            }
        }
        await reader.DisposeAsync();

        if (dropSource)
        {
            await using var drop = source.CreateCommand();
            drop.CommandText = $"DROP TABLE {LogSchema.TableName};";
            await drop.ExecuteNonQueryAsync(cancel);
        }

        return copied;
    }

    private static async Task<bool> SqlServerTableExistsAsync(SqlConnection source, CancellationToken cancel)
    {
        await using var command = source.CreateCommand();
        command.CommandText = $"SELECT 1 FROM sys.tables WHERE name = '{LogSchema.TableName}';";
        return await command.ExecuteScalarAsync(cancel) is not null;
    }

    private static async Task<bool> SqliteTableExistsAsync(SqliteConnection source, CancellationToken cancel)
    {
        await using var command = source.CreateCommand();
        command.CommandText = $"SELECT 1 FROM sqlite_master WHERE type='table' AND name='{LogSchema.TableName}';";
        return await command.ExecuteScalarAsync(cancel) is not null;
    }

    /// <summary>The whole source table is one app's rows (one <c>AddMindAtticLog</c> call per
    /// host), so the first row's <c>Application</c> value names it; an empty source has nothing to
    /// migrate and no watermark to scope by, so the caller gets back everything already in the
    /// destination for every application in that edge case — acceptable since there's nothing to
    /// copy either way.</summary>
    private static async Task<string?> ReadApplicationAsync(System.Data.Common.DbConnection source, CancellationToken cancel)
    {
        await using var command = source.CreateCommand();
        command.CommandText = $"SELECT TOP (1) Application FROM dbo.{LogSchema.TableName};";
        if (source is SqliteConnection)
            command.CommandText = $"SELECT Application FROM {LogSchema.TableName} LIMIT 1;";
        try
        {
            var result = await command.ExecuteScalarAsync(cancel);
            return result as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DateTime ReadDestinationWatermark(string destDbPath, string? application)
    {
        if (application is null || !File.Exists(destDbPath)) return DateTime.MinValue;

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destDbPath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MAX(TimestampUtc) FROM {LogSchema.TableName} WHERE Application = $application;";
        var applicationParam = command.CreateParameter();
        applicationParam.ParameterName = "$application";
        applicationParam.Value = application;
        command.Parameters.Add(applicationParam);

        var result = command.ExecuteScalar();
        return result is string s ? DateTime.Parse(s, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)
            : DateTime.MinValue;
    }

    private static SqliteConnection OpenDestination(string destDbPath)
    {
        var directory = Path.GetDirectoryName(destDbPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destDbPath }.ToString());
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = LogSchema.CreateTableSqlite;
        create.ExecuteNonQuery();
        return connection;
    }

    private static SqliteCommand CreateInsertCommand(SqliteConnection dest)
    {
        var command = dest.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {LogSchema.TableName}
                (TimestampUtc, Level, Application, Category, Message, MessageTemplate, Exception, PropertiesJson, CorrelationId, MachineName)
            VALUES
                ($timestampUtc, $level, $application, $category, $message, $messageTemplate, $exception, $propertiesJson, $correlationId, $machineName);
            """;
        foreach (var name in new[]
                 {
                     "$timestampUtc", "$level", "$application", "$category", "$message",
                     "$messageTemplate", "$exception", "$propertiesJson", "$correlationId", "$machineName",
                 })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static void BindRow(SqliteCommand insert, System.Data.Common.DbDataReader source)
    {
        insert.Parameters["$timestampUtc"].Value = ToIsoUtc(source.GetValue(0));
        insert.Parameters["$level"].Value = Convert.ToInt32(source.GetValue(1));
        insert.Parameters["$application"].Value = source.GetString(2);
        insert.Parameters["$category"].Value = (object?)(source.IsDBNull(3) ? null : source.GetString(3)) ?? DBNull.Value;
        insert.Parameters["$message"].Value = source.GetString(4);
        insert.Parameters["$messageTemplate"].Value = (object?)(source.IsDBNull(5) ? null : source.GetString(5)) ?? DBNull.Value;
        insert.Parameters["$exception"].Value = (object?)(source.IsDBNull(6) ? null : source.GetString(6)) ?? DBNull.Value;
        insert.Parameters["$propertiesJson"].Value = (object?)(source.IsDBNull(7) ? null : source.GetString(7)) ?? DBNull.Value;
        insert.Parameters["$correlationId"].Value = (object?)(source.IsDBNull(8) ? null : source.GetString(8)) ?? DBNull.Value;
        insert.Parameters["$machineName"].Value = (object?)(source.IsDBNull(9) ? null : source.GetString(9)) ?? DBNull.Value;
    }

    /// <summary>SQL Server's DATETIME2 column comes back as a native DateTime; SQLite's TEXT column
    /// is already the ISO-8601 string the sink wrote. Same bug class this repo already fixed once
    /// in MindAttic.Log.Reader/Services/LogQueryService.cs — culture-dependent ToString() must never
    /// be the round trip.</summary>
    private static string ToIsoUtc(object value) => value switch
    {
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("o"),
        string s => s,
        _ => throw new InvalidOperationException($"Unexpected TimestampUtc value type: {value.GetType()}"),
    };
}
