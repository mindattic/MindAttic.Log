using MindAttic.Log.Sinks;
using Serilog.Core;

namespace MindAttic.Log.Stores;

/// <summary>The SQL Server store (M.LOGS-T17): every app that picks it writes into one shared
/// <c>MindAttic.Log</c> database, e.g. on the local <c>MSSQLSERVER</c> instance, through
/// <see cref="MindAtticSqlServerSink"/>. An app picks it by setting
/// <see cref="MindAtticLogOptions.ConnectionString"/>.</summary>
public sealed class SqlServerLogStore : ILogStore
{
    public const string StoreName = "sqlserver";

    public string Name => StoreName;

    public ILogEventSink CreateSink(MindAtticLogOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException($"{nameof(options.ConnectionString)} is required.");

        return new MindAtticSqlServerSink(options.ConnectionString, options.Application, retainedDays: options.RetainedDays);
    }
}
