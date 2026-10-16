using MindAttic.Log.Reader.Services;
using MindAttic.Vault.Paths;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

public class LogQueryServiceDiscoverAppsTests
{
    private string rootDirectory = null!;
    private string? originalRoamingRoot;
    private readonly LogQueryService service = new();

    private string LoggingBucketDirectory => Path.Combine(rootDirectory, VaultPaths.LoggingBucket);

    [SetUp]
    public void SetUp()
    {
        rootDirectory = Path.Combine(Path.GetTempPath(), $"mindattic-log-discover-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(rootDirectory);
        originalRoamingRoot = Environment.GetEnvironmentVariable(VaultPaths.RoamingRootEnvVar);
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, rootDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, originalRoamingRoot);
        if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
    }

    private void CreateAppLogDb(string appId)
    {
        var appDirectory = Path.Combine(LoggingBucketDirectory, appId);
        Directory.CreateDirectory(appDirectory);
        File.WriteAllText(Path.Combine(appDirectory, "MindAttic.Log.db"), "");
    }

    [Test]
    public void DiscoverApps_Returns_Empty_When_The_Logging_Bucket_Is_Absent()
    {
        var results = service.DiscoverApps();

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void DiscoverApps_Lists_Only_Folders_Containing_The_Log_Db()
    {
        CreateAppLogDb("AppA");
        Directory.CreateDirectory(Path.Combine(LoggingBucketDirectory, "AppB")); // no db file inside

        var results = service.DiscoverApps();

        Assert.That(results.Select(s => s.AppId), Is.EqualTo(new[] { "AppA" }));
    }

    [Test]
    public void DiscoverApps_Orders_AppIds_Case_Insensitively()
    {
        CreateAppLogDb("bravo");
        CreateAppLogDb("Alpha");
        CreateAppLogDb("charlie");

        var results = service.DiscoverApps();

        Assert.That(results.Select(s => s.AppId), Is.EqualTo(new[] { "Alpha", "bravo", "charlie" }));
    }

    [Test]
    public void DiscoverApps_Resolves_The_Db_Path_Under_The_Apps_Folder()
    {
        CreateAppLogDb("AppA");

        var results = service.DiscoverApps();

        Assert.That(results.Single().Path, Is.EqualTo(Path.Combine(LoggingBucketDirectory, "AppA", "MindAttic.Log.db")));
    }
}
