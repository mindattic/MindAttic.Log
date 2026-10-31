using MindAttic.Vault.Settings.Manifest;

namespace MindAttic.Log.Reader.Services;

/// <summary>Roaming at <c>%APPDATA%\MindAttic\MindAttic.Log.Reader\settings.json</c>; edited in MindAttic.Vault.Desktop.</summary>
[SettingsApp(AppId, "Log Reader", Icon = "log", Description = "Viewer for every MindAttic app's log database.")]
public sealed class ReaderSettings
{
    public const string AppId = "MindAttic.Log.Reader";

    /// <summary>App id (Vault <c>Logging</c> bucket subfolder name) last viewed, so the Reader can
    /// re-select it on the next launch instead of defaulting to nothing.</summary>
    [Setting("App shown at start", Group = "General", Order = 1,
        Help = "The Reader opens this app's log when it starts (a folder name under the Logging bucket, e.g. Prose). It updates to the last app you viewed.")]
    public string? LastAppId { get; set; }
}
