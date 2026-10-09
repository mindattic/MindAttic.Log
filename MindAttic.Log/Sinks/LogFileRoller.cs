namespace MindAttic.Log.Sinks;

/// <summary>
/// Computes the rolling SQLite file path for the no-database tier and prunes old ones. Rolls
/// monthly (a queryable DB file, unlike Prose's daily text files, stays useful well past a day —
/// see docs/BIBLE.md §4 for why monthly was chosen over Prose's daily/14-file retention) and
/// prunes by retained file COUNT, mirroring Prose's <c>retainedFileCountLimit</c> idea.
/// </summary>
public static class LogFileRoller
{
    /// <summary>
    /// Builds "&lt;directory&gt;/MindAttic.Log.&lt;yyyy-MM&gt;.db" for <paramref name="utcNow"/>.
    /// </summary>
    public static string CurrentPath(string directory, DateTime utcNow) =>
        Path.Combine(directory, $"MindAttic.Log.{utcNow:yyyy-MM}.db");

    /// <summary>
    /// Deletes rolled files in <paramref name="directory"/> beyond <paramref name="retainedFileCount"/>,
    /// oldest first by filename (which sorts chronologically since it embeds yyyy-MM). Never throws —
    /// a retention sweep is best-effort housekeeping, not something that should take an app down.
    /// </summary>
    public static void PruneOldFiles(string directory, int retainedFileCount)
    {
        if (retainedFileCount <= 0 || !Directory.Exists(directory)) return;
        try
        {
            var files = Directory.GetFiles(directory, "MindAttic.Log.*.db")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .Skip(retainedFileCount);
            foreach (var file in files)
            {
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
