using MindAttic.Log.Models;

namespace MindAttic.Log;

public sealed class MindAtticLogOptions
{
    /// <summary>Required. The <c>Application</c> column value, e.g. "MindAttic.Launcher".</summary>
    public string Application { get; set; } = "";

    public LogSeverity MinimumLevel { get; set; } = LogSeverity.Information;

    /// <summary>Which registered log store this app writes to (see <c>Stores.LogStoreRegistry</c>).
    /// Left blank, the store follows the configuration: <c>"sqlserver"</c> when
    /// <see cref="ConnectionString"/> is set, otherwise <c>"sqlite"</c>, the standalone
    /// <c>MindAttic.Log.db</c> at <see cref="DbPath"/>.</summary>
    public string? Store { get; set; }

    /// <summary>
    /// The SQL Server store's connection string, read by the host from its configuration, e.g.
    /// <c>Server=localhost;Database=MindAttic.Log;Integrated Security=true;TrustServerCertificate=true</c>.
    /// Setting it moves the app onto the shared <c>MindAttic.Log</c> database; a connection string
    /// that names no database uses <c>MindAttic.Log</c>. Like <see cref="DbPath"/>, the host resolves
    /// it and passes it in (M.LOGS-LAW-2).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>The store this app actually writes to: <see cref="Store"/> when set, otherwise
    /// chosen by whether <see cref="ConnectionString"/> is configured.</summary>
    public string EffectiveStore =>
        !string.IsNullOrWhiteSpace(Store) ? Store
        : !string.IsNullOrWhiteSpace(ConnectionString) ? Stores.SqlServerLogStore.StoreName
        : Stores.LogStoreRegistry.DefaultStore;

    /// <summary>
    /// Required by the SQLite store. Full path to this app's standalone <c>MindAttic.Log.db</c>. The host computes this
    /// via <c>MindAttic.Vault.Paths.VaultPaths.LoggingDbPath(Application)</c> and passes the resolved
    /// absolute path in — the same arm's-length pattern every other Vault-resolved value in this
    /// library follows (see M.LOGS-LAW-2 in docs/BIBLE.md): the host resolves through Vault, this
    /// library never touches <c>%APPDATA%</c> itself.
    /// </summary>
    public string? DbPath { get; set; }

    /// <summary>How many days of rows to keep. A periodic sweep deletes rows older than this and
    /// reclaims the freed space (see <c>MindAtticSqliteSink</c>; in SQL Server only this app's own
    /// rows are swept, see <c>MindAtticSqlServerSink</c>). Zero or negative disables the
    /// sweep (keep everything).</summary>
    public int RetainedDays { get; set; } = 365;
}
