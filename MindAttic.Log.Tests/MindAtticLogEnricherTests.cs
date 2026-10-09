using MindAttic.Log.Sinks;
using NUnit.Framework;
using Serilog.Events;
using Serilog.Parsing;

namespace MindAttic.Log.Tests;

public class MindAtticLogEnricherTests
{
    /// <summary>
    /// Regression test for a real bug: the SQL Server sink's AdditionalColumns maps the Category
    /// column to a property literally named "Category", but the enricher only ever excluded
    /// "SourceContext" from PropertiesJson — it never copied/renamed it to "Category" — so the
    /// column was always NULL on the SQL Server tier while the SQLite tier populated it correctly.
    /// </summary>
    [Test]
    public void Enrich_Copies_SourceContext_Into_A_Category_Property()
    {
        var template = new MessageTemplateParser().Parse("Hello");
        var properties = new[] { new LogEventProperty("SourceContext", new ScalarValue("My.Namespace.MyClass")) };
        var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, template, properties);

        new MindAtticLogEnricher("TestApp").Enrich(logEvent, new DummyPropertyFactory());

        Assert.That(logEvent.Properties.TryGetValue("Category", out var category), Is.True,
            "Expected a 'Category' property so the SQL Server sink's AdditionalColumns mapping finds it.");
        Assert.That(((ScalarValue)category!).Value, Is.EqualTo("My.Namespace.MyClass"));
    }

    [Test]
    public void Enrich_Does_Not_Add_Category_When_No_SourceContext_Present()
    {
        var template = new MessageTemplateParser().Parse("Hello");
        var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, template, []);

        new MindAtticLogEnricher("TestApp").Enrich(logEvent, new DummyPropertyFactory());

        Assert.That(logEvent.Properties.ContainsKey("Category"), Is.False);
    }

    private sealed class DummyPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
