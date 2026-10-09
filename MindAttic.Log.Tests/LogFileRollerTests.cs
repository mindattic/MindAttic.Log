using MindAttic.Log.Sinks;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

public class LogFileRollerTests
{
    [Test]
    public void CurrentPath_Embeds_Year_And_Month()
    {
        var path = LogFileRoller.CurrentPath(@"C:\logs", new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc));
        Assert.That(path, Is.EqualTo(@"C:\logs\MindAttic.Log.2026-10.db"));
    }

    [Test]
    public void PruneOldFiles_Keeps_Only_The_Most_Recent_N()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mindattic-log-roller-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var month in new[] { "2026-07", "2026-08", "2026-09", "2026-10" })
                File.WriteAllText(Path.Combine(directory, $"MindAttic.Log.{month}.db"), "");

            LogFileRoller.PruneOldFiles(directory, retainedFileCount: 2);

            var remaining = Directory.GetFiles(directory, "MindAttic.Log.*.db")
                .Select(Path.GetFileName)
                .OrderBy(f => f)
                .ToArray();
            Assert.That(remaining, Is.EqualTo(new[] { "MindAttic.Log.2026-09.db", "MindAttic.Log.2026-10.db" }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void PruneOldFiles_With_Zero_Retention_Deletes_Nothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mindattic-log-roller-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "MindAttic.Log.2026-10.db"), "");
            LogFileRoller.PruneOldFiles(directory, retainedFileCount: 0);
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
