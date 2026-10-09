using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindAttic.Log.Extensions;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

public class ServiceCollectionExtensionsTests
{
    private string dbPath = null!;

    [SetUp]
    public void SetUp() => dbPath = Path.Combine(Path.GetTempPath(), $"mindattic-log-di-tests-{Guid.NewGuid()}.db");

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }

    /// <summary>
    /// AddSerilog alone (outside of ILoggingBuilder) does not register the open generic
    /// ILogger&lt;T&gt;/ILoggerFactory services — only AddLogging(builder => builder.AddSerilog(...))
    /// does. Caught originally by Automata's integration test when AddMindAtticLog used the former;
    /// kept here so the regression can't come back silently. See docs/MIGRATION.md.
    /// </summary>
    [Test]
    public void AddMindAtticLog_Makes_ILogger_Of_T_Resolvable()
    {
        var services = new ServiceCollection();
        services.AddMindAtticLog(o =>
        {
            o.Application = "MindAttic.Log.Tests";
            o.Destination = LogDestination.Sqlite;
            o.SqlitePath = dbPath;
        });

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<ServiceCollectionExtensionsTests>>();

        Assert.That(logger, Is.Not.Null);
        Assert.DoesNotThrow(() => logger.LogInformation("Resolved and callable."));
    }
}
