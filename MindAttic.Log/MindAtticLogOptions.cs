using MindAttic.Log.Models;

namespace MindAttic.Log;

public enum LogDestination
{
    /// <summary>Writes into the app's own SQL Server database (see docs/BIBLE.md §4 — SQL Server tier).</summary>
    SqlServer,

    /// <summary>Writes into a SQLite database — either the app's own <c>.db</c> file
    /// (<see cref="MindAtticLogOptions.SqlitePath"/> pointed at it) or a dedicated rolled MindAttic.Log
    /// file for apps with no database at all (leave <see cref="MindAtticLogOptions.SqlitePath"/> null
    /// and set <see cref="MindAtticLogOptions.FileDirectory"/> instead; see docs/BIBLE.md §4 —
    /// "file tier" is a SQLite tier).</summary>
    Sqlite,
}

public sealed class MindAtticLogOptions
{
    /// <summary>Required. The <c>Application</c> column value, e.g. "MindAttic.Launcher".</summary>
    public string Application { get; set; } = "";

    public LogDestination Destination { get; set; } = LogDestination.Sqlite;

    public LogSeverity MinimumLevel { get; set; } = LogSeverity.Information;

    /// <summary>SQL Server tier: a connection string, resolved by the host through MindAttic.Vault
    /// per HOUSE-LAW-3 — this library never resolves credentials itself.</summary>
    public string? SqlServerConnectionString { get; set; }

    /// <summary>Sqlite tier, app-owned database: full path to the app's existing <c>.db</c> file.
    /// The sink adds the shared <c>MindAttic_Log</c> table to it; it never touches the app's own
    /// tables.</summary>
    public string? SqlitePath { get; set; }

    /// <summary>Sqlite tier, no-database apps: directory to roll <c>MindAttic.Log.&lt;yyyy-MM&gt;.db</c>
    /// files into. Ignored when <see cref="SqlitePath"/> is set.</summary>
    public string? FileDirectory { get; set; }

    /// <summary>How many rolled files to keep when using <see cref="FileDirectory"/>. Mirrors
    /// Prose's 14-file retention idea; monthly roll with 12 means roughly a year of history.</summary>
    public int RetainedFileCount { get; set; } = 12;
}
