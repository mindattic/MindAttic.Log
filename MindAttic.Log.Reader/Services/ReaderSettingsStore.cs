using MindAttic.Vault.Settings;

namespace MindAttic.Log.Reader.Services;

/// <summary>Thin wrapper so MainWindow depends on one small type instead of the generic
/// <see cref="JsonSettingsStore{T}"/> directly — matches Automata.App's AutomataSettingsStore shape.</summary>
public sealed class ReaderSettingsStore(JsonSettingsStore<ReaderSettings> inner)
{
    /// <summary>Seeds defaults on first run and writes the manifest MindAttic.Vault.Desktop renders.</summary>
    public ReaderSettings LoadOrSeed() => inner.LoadOrSeed();
    public ReaderSettings Load() => inner.Load();
    public void Save(ReaderSettings settings) => inner.Save(settings);
}
