namespace MindAttic.Log.Reader.Services;

/// <summary>Roaming at <c>%APPDATA%\MindAttic\MindAttic.Log.Reader\settings.json</c>. A SQL
/// Server connection string is treated as a credential per HOUSE-LAW-3 — the Reader resolves it
/// through MindAttic.Vault's broker/token stores by name, never stores the raw string here.</summary>
public sealed class ReaderSettings
{
    public string? LastFolderPath { get; set; }
    public string? LastSqliteFilePath { get; set; }
    public string? LastSqlServerCredentialName { get; set; }
    public string LastSourceKind { get; set; } = "Folder";
}
