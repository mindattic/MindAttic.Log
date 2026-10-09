using System.Text.Json;
using Serilog.Core;
using Serilog.Events;

namespace MindAttic.Log.Sinks;

/// <summary>
/// Projects the shared <see cref="Schema.LogSchema"/> columns that the SQL Server sink can't
/// derive on its own (Application, MachineName, MessageTemplate text, and a JSON blob of
/// whatever structured properties the call site logged) onto the event as ordinary Serilog
/// properties, so <c>LoggerConfigurationExtensions.SqlServerColumnOptions</c>'s
/// <c>AdditionalColumns</c> can map them straight across.
/// <para>
/// Only used on the SQL Server path — <see cref="MindAtticSqliteSink"/> computes the same values
/// itself directly off the raw <see cref="LogEvent"/>, so applying this enricher there too would
/// just nest those same values a second time inside its own PropertiesJson.
/// </para>
/// </summary>
public sealed class MindAtticLogEnricher(string application) : ILogEventEnricher
{
    private static readonly HashSet<string> WellKnown = new(StringComparer.Ordinal)
    {
        "SourceContext", "CorrelationId", "Application", "MachineName", "MessageTemplate", "PropertiesJson",
    };

    private readonly string machineName = Environment.MachineName;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var extra = logEvent.Properties
            .Where(p => !WellKnown.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value.ToString());

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Application", application));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("MachineName", machineName));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("MessageTemplate", logEvent.MessageTemplate.Text));
        if (extra.Count > 0)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("PropertiesJson", JsonSerializer.Serialize(extra)));
    }
}
