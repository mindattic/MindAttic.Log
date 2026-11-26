using MindAttic.Log.Sinks;
using Serilog.Core;

namespace MindAttic.Log.Stores;

/// <summary>The default store: the app's standalone <c>MindAttic.Log.db</c> written by
/// <see cref="MindAtticSqliteSink"/> (see docs/BIBLE.md §4).</summary>
public sealed class SqliteLogStore : ILogStore
{
    public const string StoreName = "sqlite";

    public string Name => StoreName;

    public ILogEventSink CreateSink(MindAtticLogOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DbPath))
            throw new InvalidOperationException($"{nameof(options.DbPath)} is required.");

        return new MindAtticSqliteSink(options.DbPath, options.Application, retainedDays: options.RetainedDays);
    }
}
