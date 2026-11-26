using System.Collections.Concurrent;

namespace MindAttic.Log.Stores;

/// <summary>
/// The provider registry behind <see cref="MindAtticLogOptions.Store"/>: every <see cref="ILogStore"/>
/// an app can choose, keyed by its case-insensitive <see cref="ILogStore.Name"/>. SQLite is
/// registered from the start and is the default; further stores (SQL Server, ...) add themselves with
/// <see cref="Register"/> rather than growing a hard-coded switch.
/// </summary>
public static class LogStoreRegistry
{
    private static readonly ConcurrentDictionary<string, ILogStore> stores =
        new(StringComparer.OrdinalIgnoreCase) { [SqliteLogStore.StoreName] = new SqliteLogStore() };

    /// <summary>The store used when an app names none.</summary>
    public static string DefaultStore => SqliteLogStore.StoreName;

    /// <summary>The names of every registered store.</summary>
    public static IReadOnlyCollection<string> Names => stores.Keys.ToArray();

    /// <summary>Registers <paramref name="store"/> under its name, replacing any store already registered under it.</summary>
    public static void Register(ILogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(store.Name))
            throw new ArgumentException($"{nameof(ILogStore.Name)} is required.", nameof(store));
        stores[store.Name] = store;
    }

    public static bool TryGet(string name, out ILogStore store) => stores.TryGetValue(name, out store!);

    /// <summary>The store registered as <paramref name="name"/>, or <see cref="DefaultStore"/> when
    /// <paramref name="name"/> is blank. Throws <see cref="InvalidOperationException"/> for an
    /// unregistered name.</summary>
    public static ILogStore Resolve(string? name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? DefaultStore : name;
        return TryGet(key, out var store)
            ? store
            : throw new InvalidOperationException(
                $"No log store is registered as '{key}'. Registered: {string.Join(", ", Names)}.");
    }
}
