using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace MindAttic.Log.Extensions;

/// <summary>
/// The app-side integration seam. Every surveyed MindAttic app already calls
/// <c>ILogger&lt;T&gt;</c> at its call sites (docs/MIGRATION.md) — this registers a Serilog
/// pipeline under the hood so none of that call-site code has to change; only each app's own
/// ad hoc file/console writers get deleted in favor of it.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// <c>services.AddMindAtticLog(o =&gt; { o.Application = "MindAttic.Launcher"; o.DbPath = VaultPaths.LoggingDbPath("MindAttic.Launcher"); });</c>
    /// </summary>
    public static IServiceCollection AddMindAtticLog(this IServiceCollection services, Action<MindAtticLogOptions> configure)
    {
        var options = new MindAtticLogOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.Application))
            throw new InvalidOperationException($"{nameof(MindAtticLogOptions.Application)} is required.");

        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilogLevel(options.MinimumLevel))
            .WriteToMindAtticLog(options)
            .CreateLogger();

        // AddLogging(...) is required, not just AddSerilog(...): it's what registers the open
        // generic ILogger<T>/ILoggerFactory services every call site resolves. AddSerilog alone
        // (on IServiceCollection, outside of an ILoggingBuilder) registers the Serilog bridge but
        // leaves ILogger<T> unresolvable — caught by Automata's integration test, see
        // docs/MIGRATION.md.
        services.AddLogging(builder =>
        {
            builder.AddSerilog(logger, dispose: true);

            // AddLogging's default filter is Information, so without this a Debug/Verbose
            // MinimumLevel would never reach the sink through ILogger<T>. The rule is scoped to the
            // Serilog provider only (SetMinimumLevel would lower every other provider too), and is
            // added only below Information: at Information and above Serilog's own MinimumLevel
            // already filters, and a provider rule would override the host's category rules.
            if (options.MinimumLevel < Models.LogSeverity.Information)
                builder.AddFilter<SerilogLoggerProvider>(null, ToMsLevel(options.MinimumLevel));
        });
        return services;
    }

    private static LogLevel ToMsLevel(Models.LogSeverity severity) => severity switch
    {
        Models.LogSeverity.Verbose => LogLevel.Trace,
        Models.LogSeverity.Debug => LogLevel.Debug,
        Models.LogSeverity.Information => LogLevel.Information,
        Models.LogSeverity.Warning => LogLevel.Warning,
        Models.LogSeverity.Error => LogLevel.Error,
        Models.LogSeverity.Fatal => LogLevel.Critical,
        _ => LogLevel.Information,
    };

    private static Serilog.Events.LogEventLevel ToSerilogLevel(Models.LogSeverity severity) => severity switch
    {
        Models.LogSeverity.Verbose => Serilog.Events.LogEventLevel.Verbose,
        Models.LogSeverity.Debug => Serilog.Events.LogEventLevel.Debug,
        Models.LogSeverity.Information => Serilog.Events.LogEventLevel.Information,
        Models.LogSeverity.Warning => Serilog.Events.LogEventLevel.Warning,
        Models.LogSeverity.Error => Serilog.Events.LogEventLevel.Error,
        Models.LogSeverity.Fatal => Serilog.Events.LogEventLevel.Fatal,
        _ => Serilog.Events.LogEventLevel.Information,
    };
}
