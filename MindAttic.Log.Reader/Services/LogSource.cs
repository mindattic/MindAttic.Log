namespace MindAttic.Log.Reader.Services;

public enum LogSourceKind { Folder, SqliteFile, SqlServer }

/// <summary>What the user picked in the source panel — a folder of rolled
/// <c>MindAttic.Log.&lt;yyyy-MM&gt;.db</c> files, one SQLite file (an app's own database), or a
/// SQL Server connection string (resolved via MindAttic.Vault upstream, never typed in plaintext
/// by the Reader itself — see <see cref="ReaderSettingsStore"/>).</summary>
public sealed class LogSource
{
    public required LogSourceKind Kind { get; init; }
    public required string Path { get; init; }
}
