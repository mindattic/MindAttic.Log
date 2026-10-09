using System.Text.Json.Serialization;

namespace MindAttic.Log.Reader.Services;

/// <summary>Deserialized straight from the filter bar's JSON message — see wwwroot/app.js.</summary>
public sealed class LogFilter
{
    public DateTime? SinceUtc { get; set; }
    public DateTime? UntilUtc { get; set; }
    public int? MinLevel { get; set; }
    public string? Application { get; set; }
    public string? Category { get; set; }
    public string? SearchText { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int MaxResults { get; set; } = 500;
}
