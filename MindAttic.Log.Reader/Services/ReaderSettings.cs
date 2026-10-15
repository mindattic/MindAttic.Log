namespace MindAttic.Log.Reader.Services;

/// <summary>Roaming at <c>%APPDATA%\MindAttic\MindAttic.Log.Reader\settings.json</c>.</summary>
public sealed class ReaderSettings
{
    /// <summary>App id (Vault <c>Logging</c> bucket subfolder name) last viewed, so the Reader can
    /// re-select it on the next launch instead of defaulting to nothing.</summary>
    public string? LastAppId { get; set; }
}
