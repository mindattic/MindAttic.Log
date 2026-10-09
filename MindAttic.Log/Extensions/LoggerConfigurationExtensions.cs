using MindAttic.Log.Schema;
using MindAttic.Log.Sinks;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

namespace MindAttic.Log.Extensions;

public static class LoggerConfigurationExtensions
{
    /// <summary>Routes to the SQLite tier — either an app-owned database file or a rolled
    /// no-database file, matching <see cref="MindAtticLogOptions.Destination"/>.</summary>
    public static LoggerConfiguration WriteToMindAtticLog(this LoggerConfiguration loggerConfiguration, MindAtticLogOptions options)
    {
        var minimumLevel = MapLevel(options.MinimumLevel);

        return options.Destination switch
        {
            LogDestination.SqlServer => loggerConfiguration
                .Enrich.With(new MindAtticLogEnricher(options.Application))
                .WriteTo.MSSqlServer(
                    connectionString: options.SqlServerConnectionString
                        ?? throw new InvalidOperationException($"{nameof(options.SqlServerConnectionString)} is required for {LogDestination.SqlServer}."),
                    sinkOptions: new MSSqlServerSinkOptions { TableName = LogSchema.TableName, AutoCreateSqlTable = false, SchemaName = "dbo" },
                    columnOptions: SqlServerColumnOptions(),
                    restrictedToMinimumLevel: minimumLevel),

            LogDestination.Sqlite => loggerConfiguration.WriteTo.Sink(
                new MindAtticSqliteSink(ResolveSqlitePath(options), options.Application),
                restrictedToMinimumLevel: minimumLevel),

            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }

    private static string ResolveSqlitePath(MindAtticLogOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SqlitePath)) return options.SqlitePath;

        var directory = options.FileDirectory
            ?? throw new InvalidOperationException(
                $"Either {nameof(options.SqlitePath)} or {nameof(options.FileDirectory)} is required for {LogDestination.Sqlite}.");
        LogFileRoller.PruneOldFiles(directory, options.RetainedFileCount);
        return LogFileRoller.CurrentPath(directory, DateTime.UtcNow);
    }

    /// <summary>
    /// SQL Server DDL is pre-created via <see cref="LogSchema.CreateTableSqlServer"/> (callers
    /// run it once, e.g. in an app's startup/migration step) — <c>AutoCreateSqlTable</c> stays
    /// false above so the sink never improvises a schema that drifts from <see cref="LogSchema"/>.
    /// </summary>
    private static ColumnOptions SqlServerColumnOptions()
    {
        var columnOptions = new ColumnOptions();
        columnOptions.Store.Clear();
        columnOptions.Store.Add(StandardColumn.Id);
        columnOptions.Store.Add(StandardColumn.TimeStamp);
        columnOptions.Store.Add(StandardColumn.Level);
        columnOptions.Store.Add(StandardColumn.Message);
        columnOptions.Store.Add(StandardColumn.Exception);
        columnOptions.TimeStamp.ColumnName = "TimestampUtc";
        columnOptions.TimeStamp.ConvertToUtc = true;
        columnOptions.Level.StoreAsEnum = false;

        // Application/Category/MessageTemplate/CorrelationId/MachineName all ride as ordinary
        // Serilog properties (pushed by ServiceCollectionExtensions' enrichers, or by the caller
        // via LogContext for CorrelationId) and land in these typed columns instead of the
        // sink's default XML/JSON blob — keeping the SQL Server table filterable the same way
        // the SQLite sink's real columns are.
        columnOptions.AdditionalColumns =
        [
            new SqlColumn("Application", System.Data.SqlDbType.NVarChar, dataLength: 200),
            new SqlColumn("Category", System.Data.SqlDbType.NVarChar, dataLength: 400, allowNull: true),
            new SqlColumn("MessageTemplate", System.Data.SqlDbType.NVarChar, dataLength: -1, allowNull: true),
            new SqlColumn("PropertiesJson", System.Data.SqlDbType.NVarChar, dataLength: -1, allowNull: true),
            new SqlColumn("CorrelationId", System.Data.SqlDbType.NVarChar, dataLength: 100, allowNull: true),
            new SqlColumn("MachineName", System.Data.SqlDbType.NVarChar, dataLength: 200, allowNull: true),
        ];
        return columnOptions;
    }

    private static LogEventLevel MapLevel(Models.LogSeverity severity) => severity switch
    {
        Models.LogSeverity.Verbose => LogEventLevel.Verbose,
        Models.LogSeverity.Debug => LogEventLevel.Debug,
        Models.LogSeverity.Information => LogEventLevel.Information,
        Models.LogSeverity.Warning => LogEventLevel.Warning,
        Models.LogSeverity.Error => LogEventLevel.Error,
        Models.LogSeverity.Fatal => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}
