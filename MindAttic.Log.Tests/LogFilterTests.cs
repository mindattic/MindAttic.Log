using System.Text.Json;
using MindAttic.Log.Reader.Services;
using NUnit.Framework;

namespace MindAttic.Log.Tests;

/// <summary>
/// Mirrors the deserialization path MainWindow.xaml.cs actually uses for the filter bar's JSON
/// (JsonSerializerDefaults.Web — camelCase, case-insensitive, numbers-as-strings) and the shape
/// wwwroot/app.js's collectFilter() sends.
/// </summary>
public class LogFilterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public void Deserializes_CamelCase_Payload_From_AppJs()
    {
        const string json = """
            {
                "sinceUtc": "2026-01-01T00:00:00.000Z",
                "untilUtc": "2026-01-02T00:00:00.000Z",
                "minLevel": 3,
                "application": "MindAttic.Launcher",
                "category": "Widget",
                "searchText": "failed",
                "maxResults": 100
            }
            """;

        var filter = JsonSerializer.Deserialize<LogFilter>(json, JsonOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(filter.SinceUtc, Is.EqualTo(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(filter.UntilUtc, Is.EqualTo(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(filter.MinLevel, Is.EqualTo(3));
            Assert.That(filter.Application, Is.EqualTo("MindAttic.Launcher"));
            Assert.That(filter.Category, Is.EqualTo("Widget"));
            Assert.That(filter.SearchText, Is.EqualTo("failed"));
            Assert.That(filter.MaxResults, Is.EqualTo(100));
        });
    }

    [Test]
    public void Accepts_MaxResults_As_A_String()
    {
        const string json = """{ "maxResults": "250" }""";

        var filter = JsonSerializer.Deserialize<LogFilter>(json, JsonOptions)!;

        Assert.That(filter.MaxResults, Is.EqualTo(250));
    }

    [Test]
    public void MaxResults_Defaults_To_500_When_Omitted()
    {
        const string json = "{}";

        var filter = JsonSerializer.Deserialize<LogFilter>(json, JsonOptions)!;

        Assert.That(filter.MaxResults, Is.EqualTo(500));
    }

    [Test]
    public void Nullable_Fields_Default_To_Null_When_Omitted()
    {
        const string json = "{}";

        var filter = JsonSerializer.Deserialize<LogFilter>(json, JsonOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(filter.SinceUtc, Is.Null);
            Assert.That(filter.UntilUtc, Is.Null);
            Assert.That(filter.MinLevel, Is.Null);
            Assert.That(filter.Application, Is.Null);
            Assert.That(filter.Category, Is.Null);
            Assert.That(filter.SearchText, Is.Null);
        });
    }

    [Test]
    public void Null_Fields_From_AppJs_Deserialize_To_Null()
    {
        // collectFilter() sends explicit nulls (e.g. `application: null`), not omitted keys, when a
        // field is left blank in the UI.
        const string json = """
            { "sinceUtc": null, "application": null, "category": null, "searchText": null, "minLevel": null }
            """;

        var filter = JsonSerializer.Deserialize<LogFilter>(json, JsonOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(filter.SinceUtc, Is.Null);
            Assert.That(filter.Application, Is.Null);
            Assert.That(filter.Category, Is.Null);
            Assert.That(filter.SearchText, Is.Null);
            Assert.That(filter.MinLevel, Is.Null);
        });
    }
}
