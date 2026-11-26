using System.Text.Json;
using Serilog.Events;

namespace MindAttic.Log.Sinks;

/// <summary>How a Serilog <see cref="LogEvent"/> maps onto the <see cref="Schema.LogSchema"/> columns,
/// shared by every store's sink so a row reads the same whichever store wrote it.</summary>
internal static class LogEventFields
{
    public static Models.LogSeverity MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => Models.LogSeverity.Verbose,
        LogEventLevel.Debug => Models.LogSeverity.Debug,
        LogEventLevel.Information => Models.LogSeverity.Information,
        LogEventLevel.Warning => Models.LogSeverity.Warning,
        LogEventLevel.Error => Models.LogSeverity.Error,
        LogEventLevel.Fatal => Models.LogSeverity.Fatal,
        _ => Models.LogSeverity.Information,
    };

    public static string? SourceContextOf(LogEvent evt) =>
        evt.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string s } ? s : null;

    public static string? CorrelationIdOf(LogEvent evt) =>
        evt.Properties.TryGetValue("CorrelationId", out var value) && value is ScalarValue { Value: string s } ? s : null;

    public static string? PropertiesOf(LogEvent evt)
    {
        var extra = evt.Properties
            .Where(p => p.Key is not ("SourceContext" or "CorrelationId"))
            .ToDictionary(p => p.Key, p => p.Value.ToString());
        return extra.Count == 0 ? null : JsonSerializer.Serialize(extra);
    }
}
