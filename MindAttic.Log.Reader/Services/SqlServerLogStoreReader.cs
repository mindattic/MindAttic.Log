using System.Data;
using Microsoft.Data.SqlClient;
using MindAttic.Log.Models;
using MindAttic.Log.Schema;
using MindAttic.Log.Sinks;
using MindAttic.Log.Stores;

namespace MindAttic.Log.Reader.Services;

/// <summary>Reads the <see cref="SqlServerLogStore"/>: the shared <c>MindAttic.Log</c> databases
/// named in <see cref="ReaderSettings.SqlServerDatabases"/>. Many apps share one database, told apart
/// by the <c>Application</c> column, so each distinct value is its own source and every query is
/// scoped to it. The Reader only ever SELECTs; it never creates the database or table.</summary>
public sealed class SqlServerLogStoreReader : ILogStoreReader
{
    public string Store => SqlServerLogStore.StoreName;

    public IReadOnlyList<LogSource> Discover(ReaderSettings settings)
    {
        var sources = new List<LogSource>();
        foreach (var configured in ConnectionStrings(settings))
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(configured);
                if (string.IsNullOrWhiteSpace(builder.InitialCatalog)) builder.InitialCatalog = MindAtticSqlServerSink.DefaultDatabase;
                var label = $"{builder.DataSource}/{builder.InitialCatalog}";

                using var connection = new SqlConnection(builder.ConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"""
                    IF OBJECT_ID(N'dbo.{LogSchema.TableName}', N'U') IS NOT NULL
                        SELECT DISTINCT Application FROM dbo.{LogSchema.TableName} ORDER BY Application;
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    sources.Add(new LogSource { Store = Store, AppId = reader.GetString(0), Path = builder.ConnectionString, Label = label });
            }
            catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException)
            {
                // A database that is down, unreachable or badly configured just contributes no apps.
            }
        }
        return sources;
    }

    public async Task<IReadOnlyList<LogEntry>> QueryAsync(LogSource source, LogFilter filter, CancellationToken cancel = default)
    {
        try
        {
            await using var connection = new SqlConnection(source.Path);
            await connection.OpenAsync(cancel);

            await using var command = connection.CreateCommand();
            var where = BuildWhere(filter, command.Parameters);
            command.Parameters.Add("@source", SqlDbType.NVarChar, 200).Value = source.AppId;
            command.Parameters.Add("@maxResults", SqlDbType.Int).Value = filter.MaxResults;
            command.CommandText = $"""
                SELECT TOP (@maxResults) {LogRows.SelectColumns}
                FROM dbo.{LogSchema.TableName}
                WHERE Application = @source{where}
                ORDER BY TimestampUtc DESC;
                """;

            var results = new List<LogEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancel);
            while (await reader.ReadAsync(cancel)) results.Add(LogRows.Read(reader));
            return results;
        }
        catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException)
        {
            return []; // unreachable server or not a MindAttic.Log database — skip quietly
        }
    }

    /// <summary>One connection string per non-blank line of <see cref="ReaderSettings.SqlServerDatabases"/>.</summary>
    private static IEnumerable<string> ConnectionStrings(ReaderSettings settings) =>
        (settings.SqlServerDatabases ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string BuildWhere(LogFilter filter, SqlParameterCollection parameters)
    {
        var clauses = new List<string>();

        if (filter.SinceUtc is { } since) { clauses.Add("TimestampUtc >= @since"); AddTimestamp(parameters, "@since", since); }
        if (filter.UntilUtc is { } until) { clauses.Add("TimestampUtc <= @until"); AddTimestamp(parameters, "@until", until); }
        if (filter.MinLevel is { } minLevel) { clauses.Add("Level >= @minLevel"); parameters.Add("@minLevel", SqlDbType.Int).Value = minLevel; }
        if (!string.IsNullOrWhiteSpace(filter.Application)) { clauses.Add("Application = @application"); parameters.Add("@application", SqlDbType.NVarChar, 200).Value = filter.Application; }
        if (!string.IsNullOrWhiteSpace(filter.Category)) { clauses.Add("Category LIKE @category ESCAPE '\\'"); parameters.Add("@category", SqlDbType.NVarChar, -1).Value = $"%{LogRows.EscapeLike(filter.Category)}%"; }
        if (!string.IsNullOrWhiteSpace(filter.SearchText)) { clauses.Add("Message LIKE @searchText ESCAPE '\\'"); parameters.Add("@searchText", SqlDbType.NVarChar, -1).Value = $"%{LogRows.EscapeLike(filter.SearchText)}%"; }

        return string.Concat(clauses.Select(c => " AND " + c));
    }

    private static void AddTimestamp(SqlParameterCollection parameters, string name, DateTime value)
    {
        var parameter = parameters.Add(name, SqlDbType.DateTime2);
        parameter.Scale = 3;
        parameter.Value = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
    }
}
