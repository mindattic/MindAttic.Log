using System.IO;
using Microsoft.Data.Sqlite;
using MindAttic.Log.Models;
using MindAttic.Log.Schema;
using MindAttic.Log.Stores;
using MindAttic.Vault.Paths;

namespace MindAttic.Log.Reader.Services;

/// <summary>Reads the <see cref="SqliteLogStore"/>: each app's standalone <c>MindAttic.Log.db</c>
/// (see docs/BIBLE.md §4). Opens connections read-only (the Reader is a viewer, never a writer, and
/// WAL mode means it can read while an app keeps logging — see
/// MindAttic.Log/Sinks/MindAtticSqliteSink.cs).</summary>
public sealed class SqliteLogStoreReader : ILogStoreReader
{
    public string Store => SqliteLogStore.StoreName;

    /// <summary>Every app with a database under Vault's <c>Logging</c> bucket
    /// (<c>%APPDATA%\MindAttic\Logging\&lt;appId&gt;\MindAttic.Log.db</c>).</summary>
    public IReadOnlyList<LogSource> Discover(ReaderSettings settings)
    {
        var bucket = VaultPaths.RoamingBucket(VaultPaths.LoggingBucket);
        if (!Directory.Exists(bucket)) return [];

        var sources = new List<LogSource>();
        foreach (var appDirectory in Directory.GetDirectories(bucket).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var appId = System.IO.Path.GetFileName(appDirectory);
            var dbPath = System.IO.Path.Combine(appDirectory, "MindAttic.Log.db");
            if (File.Exists(dbPath)) sources.Add(new LogSource { Store = Store, AppId = appId, Path = dbPath });
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
        try
        {
            await connection.OpenAsync(cancel);

            var (where, parameters) = BuildWhere(filter);
            var sql = $"""
                SELECT {LogRows.SelectColumns}
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
            while (await reader.ReadAsync(cancel)) results.Add(LogRows.Read(reader));
            return results;
        }
        catch (SqliteException) { return []; } // e.g. not a MindAttic.Log database — skip quietly
    }

    private static (string Where, List<(string Name, object Value)> Parameters) BuildWhere(LogFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new List<(string, object)>();

        if (filter.SinceUtc is { } since) { clauses.Add("TimestampUtc >= $since"); parameters.Add(("$since", since.ToString("o"))); }
        if (filter.UntilUtc is { } until) { clauses.Add("TimestampUtc <= $until"); parameters.Add(("$until", until.ToString("o"))); }
        if (filter.MinLevel is { } minLevel) { clauses.Add("Level >= $minLevel"); parameters.Add(("$minLevel", minLevel)); }
        if (!string.IsNullOrWhiteSpace(filter.Application)) { clauses.Add("Application = $application"); parameters.Add(("$application", filter.Application)); }
        if (!string.IsNullOrWhiteSpace(filter.Category)) { clauses.Add("Category LIKE $category ESCAPE '\\'"); parameters.Add(("$category", $"%{LogRows.EscapeLike(filter.Category)}%")); }
        if (!string.IsNullOrWhiteSpace(filter.SearchText)) { clauses.Add("Message LIKE $searchText ESCAPE '\\'"); parameters.Add(("$searchText", $"%{LogRows.EscapeLike(filter.SearchText)}%")); }

        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        return (where, parameters);
    }
}
