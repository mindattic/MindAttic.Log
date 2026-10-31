using MindAttic.Log.Reader.Services;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

/// <summary>Every test in this assembly sees temp Vault roots, so nothing can write the real AppData.</summary>
[SetUpFixture]
public sealed class TestVaultRoots
{
    private string root = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "MindAtticLogVault-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, Path.Combine(root, "roaming"));
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, Path.Combine(root, "local"));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, null);
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, null);
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

/// <summary>LAMP (M.LOGS-T20): the Reader's settings described for MindAttic.Vault.Desktop.</summary>
[NonParallelizable]
public class LampReaderSettingsTests
{
    private static string Folder => VaultPaths.RoamingBucket(ReaderSettings.AppId);
    private static ReaderSettingsStore NewStore() => new(new JsonSettingsStore<ReaderSettings>(Folder));

    [SetUp]
    public void SetUp()
    {
        if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
    }

    [Test]
    public void Setting_LastAppId_ImportsRoundTripsAndIsWhatTheReaderReopens()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "settings.json"), """{ "lastAppId": "Prose" }""");
        Assert.That(NewStore().LoadOrSeed().LastAppId, Is.EqualTo("Prose"), "today's value carried over");

        // What MainWindow does when you pick an app, then what it posts to the page on the next start.
        var store = NewStore();
        var s = store.Load();
        s.LastAppId = "Tutor";
        store.Save(s);
        Assert.That(NewStore().Load().LastAppId, Is.EqualTo("Tutor"));

        var field = SettingsManifestBuilder.Build<ReaderSettings>().Find("MindAttic:Settings:MindAttic.Log.Reader:lastAppId");
        Assert.That(field!.Kind, Is.EqualTo(SettingKind.Text));
    }

    [Test]
    public void EverySettingHasItsOwnTest()
    {
        var tested = GetType().GetMethods().Select(m => m.Name).Where(n => n.StartsWith("Setting_")).ToList();
        foreach (var field in SettingsManifestBuilder.Build<ReaderSettings>().Fields)
            Assert.That(tested.Any(t => t.StartsWith("Setting_" + char.ToUpperInvariant(field.Property[0]) + field.Property[1..] + "_")), Is.True, field.Property);
    }

    [Test]
    public void Manifest_IsLabeledRoamingAndSecretFree()
    {
        var m = SettingsManifestBuilder.Build<ReaderSettings>();
        Assert.That(m.AppId, Is.EqualTo("MindAttic.Log.Reader"));
        Assert.That(m.Scope, Is.EqualTo(SettingsManifest.RoamingScope));
        Assert.That(m.Fields.All(f => !string.IsNullOrWhiteSpace(f.Label) && !string.IsNullOrWhiteSpace(f.Help)), Is.True);
        Assert.That(m.Credentials, Is.Empty);
    }

    [Test]
    public void LoadOrSeed_OnAFreshMachine_SeedsDefaultsAndTheManifest()
    {
        Assert.That(NewStore().LoadOrSeed().LastAppId, Is.Null);
        Assert.That(File.Exists(Path.Combine(Folder, SettingsManifest.FileName)), Is.True);
    }

    [Test]
    public void WriteSettingsManifestSwitch_UsesTheSameFolderAsTheApp()
    {
        var output = new StringWriter();
        Assert.That(SettingsManifestCommand.TryRun<ReaderSettings>(new[] { SettingsManifestCommand.Switch }, output), Is.True);
        Assert.That(output.ToString().Trim(), Is.EqualTo(Path.Combine(Folder, SettingsManifest.FileName)));
    }
}
