namespace MindAttic.Log.Models;

/// <summary>
/// The one strict wire schema every MindAttic app writes and every sink (SQL Server, SQLite,
/// or the SQLite-backed file tier) persists verbatim. See docs/BIBLE.md §4 for the rationale
/// behind each column, derived from auditing Prose's existing Serilog file + LogIssue tables
/// plus the ecosystem survey in docs/MIGRATION.md.
/// </summary>
public sealed class LogEntry
{
    public long Id { get; set; }

    /// <summary>Always UTC. Callers never write local time.</summary>
    public DateTime TimestampUtc { get; set; }

    public LogSeverity Level { get; set; }

    /// <summary>PackageId/AssemblyName of the writing app, e.g. "MindAttic.Launcher". Lets one
    /// Reader session show logs from multiple apps side by side without ambiguity.</summary>
    public string Application { get; set; } = "";

    /// <summary>The ILogger&lt;T&gt; category (fully-qualified source type name), e.g.
    /// "MindAttic.Launcher.Commands.HostAgentCommand". Nullable because some writers (raw
    /// Serilog.Log.Logger calls) never set a category; Prose's file tier is an example of a
    /// pipeline that historically dropped this.</summary>
    public string? Category { get; set; }

    /// <summary>Fully rendered message text.</summary>
    public string Message { get; set; } = "";

    /// <summary>Serilog's message template (e.g. "User {UserId} logged in"), kept alongside the
    /// rendered message so a Reader can group/query by shape, not just exact text.</summary>
    public string? MessageTemplate { get; set; }

    /// <summary>Full exception text (ToString()), multi-line, nullable.</summary>
    public string? Exception { get; set; }

    /// <summary>Serilog structured properties serialized as a JSON object, nullable. Keeps the
    /// column count fixed regardless of what any given call site logs.</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>Caller-supplied correlation/operation id for tracing one logical operation
    /// across log lines (and, for web apps, across services). Nullable — not every app has a
    /// notion of a request/operation id.</summary>
    public string? CorrelationId { get; set; }

    public string? MachineName { get; set; }
}
