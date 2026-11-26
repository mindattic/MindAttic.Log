using MindAttic.Log.Stores;

namespace MindAttic.Log.Reader.Services;

/// <summary>One app's logs in one store (M.LOGS-T31). <see cref="Store"/> is the
/// <see cref="ILogStore.Name"/> the logs were written through, and picks the
/// <see cref="ILogStoreReader"/> that queries them. <see cref="AppId"/> is the app: the folder
/// name under Vault's <c>Logging</c> bucket for SQLite, the <c>Application</c> column value for
/// SQL Server. <see cref="Path"/> is where the store lives: the absolute <c>MindAttic.Log.db</c>
/// path, or the SQL Server connection string, kept alongside so a query never re-derives it.</summary>
public sealed class LogSource
{
    public string Store { get; init; } = SqliteLogStore.StoreName;
    public required string AppId { get; init; }
    public required string Path { get; init; }

    /// <summary>What the Reader shows for where the logs live; <see cref="Path"/> when unset.</summary>
    public string? Label { get; init; }

    /// <summary>Tells apart the same app logged to more than one store; a SQLite source keeps its
    /// bare <see cref="AppId"/> so a saved <see cref="ReaderSettings.LastAppId"/> still matches.</summary>
    public string Key => string.Equals(Store, SqliteLogStore.StoreName, StringComparison.OrdinalIgnoreCase)
        ? AppId
        : $"{AppId} ({Label ?? Store})";
}
