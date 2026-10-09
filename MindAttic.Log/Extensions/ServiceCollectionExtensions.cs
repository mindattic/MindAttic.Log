using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

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
    /// <c>services.AddMindAtticLog(o =&gt; { o.Application = "MindAttic.Launcher"; o.Destination = LogDestination.Sqlite; o.FileDirectory = ...; });</c>
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
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: true));
        return services;
    }

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
