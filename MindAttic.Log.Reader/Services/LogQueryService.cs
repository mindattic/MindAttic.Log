using MindAttic.Log.Models;

namespace MindAttic.Log.Reader.Services;

/// <summary>
/// Read-only query layer over every app's logs, whichever <see cref="MindAttic.Log.Stores.ILogStore"/>
/// they were written through (M.LOGS-T31): standalone SQLite files under Vault's <c>Logging</c> bucket
/// and the SQL Server databases configured in <see cref="ReaderSettings.SqlServerDatabases"/>. Each
/// store's discovery and querying live in its <see cref="ILogStoreReader"/>.
/// </summary>
public sealed class LogQueryService(ReaderSettingsStore? settingsStore = null)
{
    private readonly IReadOnlyList<ILogStoreReader> readers = [new SqliteLogStoreReader(), new SqlServerLogStoreReader()];

    /// <summary>Lists every app with logs in any store, ordered by app id — this is what drives the
    /// Reader's app switcher instead of a manual folder/file picker.</summary>
    public IReadOnlyList<LogSource> DiscoverApps()
    {
        var settings = settingsStore?.Load() ?? new ReaderSettings();
        return readers
            .SelectMany(reader => reader.Discover(settings))
            .OrderBy(source => source.AppId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<IReadOnlyList<LogEntry>> QueryAsync(LogSource source, LogFilter filter, CancellationToken cancel = default)
    {
        var reader = readers.FirstOrDefault(r => string.Equals(r.Store, source.Store, StringComparison.OrdinalIgnoreCase));
        return reader?.QueryAsync(source, filter, cancel) ?? Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }
}
