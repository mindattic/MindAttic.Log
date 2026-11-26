using MindAttic.Log.Stores;
using Serilog;
using Serilog.Events;

namespace MindAttic.Log.Extensions;

public static class LoggerConfigurationExtensions
{
    /// <summary>Routes the app onto the log store <see cref="MindAtticLogOptions.EffectiveStore"/> names in
    /// <see cref="LogStoreRegistry"/>: SQL Server when a connection string is configured, otherwise
    /// its standalone SQLite <c>MindAttic.Log.db</c> (see docs/BIBLE.md §4).</summary>
    public static LoggerConfiguration WriteToMindAtticLog(this LoggerConfiguration loggerConfiguration, MindAtticLogOptions options)
    {
        var store = LogStoreRegistry.Resolve(options.EffectiveStore);
        return loggerConfiguration.WriteTo.Sink(
            store.CreateSink(options),
            restrictedToMinimumLevel: MapLevel(options.MinimumLevel));
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
