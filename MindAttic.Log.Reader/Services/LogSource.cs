namespace MindAttic.Log.Reader.Services;

/// <summary>One app's standalone <c>MindAttic.Log.db</c> — the only tier left (see
/// docs/BIBLE.md §1/§4). <see cref="AppId"/> is the folder name under Vault's <c>Logging</c>
/// bucket (<c>VaultPaths.LoggingDbPath</c>'s first segment); <see cref="Path"/> is the resolved
/// absolute file path, kept alongside it so a query never has to re-derive it.</summary>
public sealed class LogSource
{
    public required string AppId { get; init; }
    public required string Path { get; init; }
}
