using Serilog.Core;

namespace MindAttic.Log.Stores;

/// <summary>
/// One kind of place an app's logs can live (M.LOGS-T17). A store is registered under its
/// <see cref="Name"/> in <see cref="LogStoreRegistry"/> and picked per app by
/// <see cref="MindAtticLogOptions.Store"/>; <see cref="SqliteLogStore"/> is the default.
/// </summary>
public interface ILogStore
{
    /// <summary>The registry key apps select this store by, e.g. <c>"sqlite"</c>. Case-insensitive.</summary>
    string Name { get; }

    /// <summary>Validates the store-specific parts of <paramref name="options"/> and builds the sink
    /// that writes this app's events into the store. Throws <see cref="InvalidOperationException"/>
    /// when a setting the store needs is missing.</summary>
    ILogEventSink CreateSink(MindAtticLogOptions options);
}
