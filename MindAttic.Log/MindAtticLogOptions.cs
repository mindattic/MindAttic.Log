using MindAttic.Log.Models;

namespace MindAttic.Log;

public sealed class MindAtticLogOptions
{
    /// <summary>Required. The <c>Application</c> column value, e.g. "MindAttic.Launcher".</summary>
    public string Application { get; set; } = "";

    public LogSeverity MinimumLevel { get; set; } = LogSeverity.Information;

    /// <summary>
    /// Required. Full path to this app's standalone <c>MindAttic.Log.db</c>. The host computes this
    /// via <c>MindAttic.Vault.Paths.VaultPaths.LoggingDbPath(Application)</c> and passes the resolved
    /// absolute path in — the same arm's-length pattern every other Vault-resolved value in this
    /// library follows (see LOG-LAW-2 in docs/BIBLE.md): the host resolves through Vault, this
    /// library never touches <c>%APPDATA%</c> itself.
    /// </summary>
    public string? DbPath { get; set; }

    /// <summary>How many days of rows to keep. A periodic sweep deletes rows older than this and
    /// reclaims the freed space (see <c>MindAtticSqliteSink</c>). Zero or negative disables the
    /// sweep (keep everything).</summary>
    public int RetainedDays { get; set; } = 365;
}
