using System.IO;
using Microsoft.Data.Sqlite;
using MindAttic.Log.Models;
using MindAttic.Log.Schema;
using MindAttic.Vault.Paths;

namespace MindAttic.Log.Reader.Services;

/// <summary>
/// Read-only query layer over an app's standalone <c>MindAttic.Log.db</c> — the only tier left
/// (see docs/BIBLE.md §1/§4). Opens SQLite connections read-only (the Reader is a viewer, never a
/// writer, and WAL mode means it can read while an app keeps logging — see
/// MindAttic.Log/Sinks/MindAtticSqliteSink.cs).
/// </summary>
public sealed class LogQueryService
{
    /// <summary>Lists every app with a standalone log database under Vault's <c>Logging</c> bucket
    /// (<c>%APPDATA%\MindAttic\Logging\&lt;appId&gt;\MindAttic.Log.db</c>) — this is what drives the
    /// Reader's app switcher instead of a manual folder/file picker.</summary>
    public IReadOnlyList<LogSource> DiscoverApps()
    {
        var bucket = VaultPaths.RoamingBucket(VaultPaths.LoggingBucket);
        if (!Directory.Exists(bucket)) return [];

        var sources = new List<LogSource>();
        foreach (var appDirectory in Directory.GetDirectories(bucket).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var appId = Path.GetFileName(appDirectory);
            var dbPath = Path.Combine(appDirectory, "MindAttic.Log.db");
            if (File.Exists(dbPath)) sources.Add(new LogSource { AppId = appId, Path = dbPath });
        }
        return sources;
    }

    public async Task<IReadOnlyList<LogEntry>> QueryAsync(LogSource source, LogFilter filter, CancellationToken cancel = default)
    {
        if (!File.Exists(source.Path)) return [];

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = source.Path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        try { await connection.OpenAsync(cancel); }
        catch (SqliteException) { return []; } // e.g. not a MindAttic.Log database — skip quietly

        var (where, parameters) = BuildWhere(filter);
        var sql = $"""
            SELECT Id, TimestampUtc, Level, Application, Category, Message, MessageTemplate, Exception, PropertiesJson, CorrelationId, MachineName
            FROM {LogSchema.TableName}
            {where}
            ORDER BY TimestampUtc DESC
            LIMIT $maxResults;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.Parameters.AddWithValue("$maxResults", filter.MaxResults);

        var results = new List<LogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel)) results.Add(ReadEntry(reader));
        return results;
    }

    private static (string Where, List<(string Name, object Value)> Parameters) BuildWhere(LogFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new List<(string, object)>();

        if (filter.SinceUtc is { } since) { clauses.Add("TimestampUtc >= $since"); parameters.Add(("$since", since.ToString("o"))); }
        if (filter.UntilUtc is { } until) { clauses.Add("TimestampUtc <= $until"); parameters.Add(("$until", until.ToString("o"))); }
        if (filter.MinLevel is { } minLevel) { clauses.Add("Level >= $minLevel"); parameters.Add(("$minLevel", minLevel)); }
        if (!string.IsNullOrWhiteSpace(filter.Application)) { clauses.Add("Application = $application"); parameters.Add(("$application", filter.Application)); }
        if (!string.IsNullOrWhiteSpace(filter.Category)) { clauses.Add("Category LIKE $category"); parameters.Add(("$category", $"%{filter.Category}%")); }
        if (!string.IsNullOrWhiteSpace(filter.SearchText)) { clauses.Add("Message LIKE $searchText"); parameters.Add(("$searchText", $"%{filter.SearchText}%")); }

        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        return (where, parameters);
    }

    private static LogEntry ReadEntry(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        TimestampUtc = DateTime.Parse(reader.GetString(1), null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
        Level = (LogSeverity)reader.GetInt32(2),
        Application = reader.GetString(3),
        Category = reader.IsDBNull(4) ? null : reader.GetString(4),
        Message = reader.GetString(5),
        MessageTemplate = reader.IsDBNull(6) ? null : reader.GetString(6),
        Exception = reader.IsDBNull(7) ? null : reader.GetString(7),
        PropertiesJson = reader.IsDBNull(8) ? null : reader.GetString(8),
        CorrelationId = reader.IsDBNull(9) ? null : reader.GetString(9),
        MachineName = reader.IsDBNull(10) ? null : reader.GetString(10),
    };
}
