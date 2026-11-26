using MindAttic.Log.Models;
using MindAttic.Log.Stores;

namespace MindAttic.Log.Reader.Services;

/// <summary>The Reader's side of an <see cref="ILogStore"/> (M.LOGS-T31): finds the apps that logged
/// into that kind of store and queries one of them, read-only. <see cref="LogQueryService"/> asks
/// every reader for its sources and sends each query to the reader whose <see cref="Store"/> matches
/// <see cref="LogSource.Store"/>.</summary>
public interface ILogStoreReader
{
    /// <summary>The <see cref="ILogStore.Name"/> this reader understands, e.g. <c>"sqlite"</c>.</summary>
    string Store { get; }

    /// <summary>Every app with logs in this kind of store. A store that can't be reached is skipped, never thrown.</summary>
    IReadOnlyList<LogSource> Discover(ReaderSettings settings);

    /// <summary>The newest entries of <paramref name="source"/> matching <paramref name="filter"/>;
    /// empty when the store is missing, unreachable or not a MindAttic.Log store.</summary>
    Task<IReadOnlyList<LogEntry>> QueryAsync(LogSource source, LogFilter filter, CancellationToken cancel = default);
}
