using MindAttic.Log.Sinks;
using Serilog;
using Serilog.Events;

namespace MindAttic.Log.Extensions;

public static class LoggerConfigurationExtensions
{
    /// <summary>Routes every app onto its standalone <see cref="MindAtticSqliteSink"/> — the one
    /// remaining tier (see docs/BIBLE.md §4).</summary>
    public static LoggerConfiguration WriteToMindAtticLog(this LoggerConfiguration loggerConfiguration, MindAtticLogOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DbPath))
            throw new InvalidOperationException($"{nameof(options.DbPath)} is required.");

        return loggerConfiguration.WriteTo.Sink(
            new MindAtticSqliteSink(options.DbPath, options.Application, retainedDays: options.RetainedDays),
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
