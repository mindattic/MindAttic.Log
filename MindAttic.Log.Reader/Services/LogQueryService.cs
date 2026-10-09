using System.IO;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MindAttic.Log.Models;
using MindAttic.Log.Schema;

namespace MindAttic.Log.Reader.Services;

/// <summary>
/// Read-only query layer over any MindAttic.Log tier. Opens SQLite connections read-only (the
/// Reader is a viewer, never a writer, and WAL mode means it can read while an app keeps logging —
/// see MindAttic.Log/Sinks/MindAtticSqliteSink.cs) and, for the folder source, only opens the
/// rolled files whose embedded yyyy-MM could possibly overlap the requested date range instead of
/// scanning every file in the directory.
/// </summary>
public sealed class LogQueryService
{
    public async Task<IReadOnlyList<LogEntry>> QueryAsync(LogSource source, LogFilter filter, CancellationToken cancel = default)
    {
        return source.Kind switch
        {
            LogSourceKind.SqlServer => await QuerySqlServerAsync(source.Path, filter, cancel),
            LogSourceKind.SqliteFile => await QuerySqliteFileAsync(source.Path, filter, cancel),
            LogSourceKind.Folder => await QueryFolderAsync(source.Path, filter, cancel),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
    }

    private async Task<IReadOnlyList<LogEntry>> QueryFolderAsync(string folder, LogFilter filter, CancellationToken cancel)
    {
        if (!Directory.Exists(folder)) return [];

        var files = Directory.GetFiles(folder, "MindAttic.Log.*.db")
            .Where(f => OverlapsRange(f, filter))
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var merged = new List<LogEntry>();
        foreach (var file in files)
        {
            merged.AddRange(await QuerySqliteFileAsync(file, filter, cancel));
            if (merged.Count >= filter.MaxResults) break;
        }
        return merged
            .OrderByDescending(e => e.TimestampUtc)
            .Take(filter.MaxResults)
            .ToList();
    }

    /// <summary>A rolled file's name embeds its yyyy-MM (see LogFileRoller.CurrentPath); a file
    /// whose whole month falls outside [SinceUtc, UntilUtc] cannot contain a matching row, so it's
    /// skipped without ever being opened.</summary>
    private static bool OverlapsRange(string filePath, LogFilter filter)
    {
        var name = Path.GetFileNameWithoutExtension(filePath); // "MindAttic.Log.2026-10"
        var parts = name.Split('.');
        if (parts.Length == 0 || !DateTime.TryParseExact(parts[^1], "yyyy-MM", null,
                System.Globalization.DateTimeStyles.None, out var monthStart))
            return true; // unrecognized name shape — don't silently drop it, just open it

        var monthEnd = monthStart.AddMonths(1);
        if (filter.UntilUtc is { } until && monthStart > until) return false;
        if (filter.SinceUtc is { } since && monthEnd <= since) return false;
        return true;
    }

    private async Task<IReadOnlyList<LogEntry>> QuerySqliteFileAsync(string path, LogFilter filter, CancellationToken cancel)
    {
        if (!File.Exists(path)) return [];

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        try { await connection.OpenAsync(cancel); }
        catch (SqliteException) { return []; } // e.g. not a MindAttic.Log database — skip quietly

        var (where, parameters) = BuildWhere(filter, sqlServer: false);
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

    private async Task<IReadOnlyList<LogEntry>> QuerySqlServerAsync(string connectionString, LogFilter filter, CancellationToken cancel)
    {
        var (where, parameters) = BuildWhere(filter, sqlServer: true);
        var sql = $"""
            SELECT TOP (@maxResults) Id, TimestampUtc, Level, Application, Category, Message, MessageTemplate, Exception, PropertiesJson, CorrelationId, MachineName
            FROM dbo.{LogSchema.TableName}
            {where}
            ORDER BY TimestampUtc DESC;
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancel);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.Parameters.AddWithValue("@maxResults", filter.MaxResults);

        var results = new List<LogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel)) results.Add(ReadEntry(reader));
        return results;
    }

    private static (string Where, List<(string Name, object Value)> Parameters) BuildWhere(LogFilter filter, bool sqlServer)
    {
        var prefix = sqlServer ? "@" : "$";
        var clauses = new List<string>();
        var parameters = new List<(string, object)>();

        if (filter.SinceUtc is { } since) { clauses.Add($"TimestampUtc >= {prefix}since"); parameters.Add(($"{prefix}since", sqlServer ? since : since.ToString("o"))); }
        if (filter.UntilUtc is { } until) { clauses.Add($"TimestampUtc <= {prefix}until"); parameters.Add(($"{prefix}until", sqlServer ? until : until.ToString("o"))); }
        if (filter.MinLevel is { } minLevel) { clauses.Add($"Level >= {prefix}minLevel"); parameters.Add(($"{prefix}minLevel", minLevel)); }
        if (!string.IsNullOrWhiteSpace(filter.Application)) { clauses.Add($"Application = {prefix}application"); parameters.Add(($"{prefix}application", filter.Application)); }
        if (!string.IsNullOrWhiteSpace(filter.Category)) { clauses.Add($"Category LIKE {prefix}category"); parameters.Add(($"{prefix}category", $"%{filter.Category}%")); }
        if (!string.IsNullOrWhiteSpace(filter.SearchText)) { clauses.Add($"Message LIKE {prefix}searchText"); parameters.Add(($"{prefix}searchText", $"%{filter.SearchText}%")); }

        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        return (where, parameters);
    }

    private static LogEntry ReadEntry(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        TimestampUtc = DateTime.Parse(reader.GetValue(1).ToString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
        Level = (LogSeverity)Convert.ToInt32(reader.GetValue(2)),
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
