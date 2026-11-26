using System.Data.Common;
using System.Globalization;
using MindAttic.Log.Models;
using MindAttic.Log.Schema;

namespace MindAttic.Log.Reader.Services;

/// <summary>What every <see cref="ILogStoreReader"/> shares: the <see cref="LogSchema.Columns"/> it
/// selects and how a row of them becomes a <see cref="LogEntry"/>, whichever store it came from.</summary>
internal static class LogRows
{
    public static readonly string SelectColumns = string.Join(", ", LogSchema.Columns);

    /// <summary>The user's text is a literal substring, so LIKE's own wildcards (% and _) and the escape character are escaped (paired with ESCAPE backslash).</summary>
    public static string EscapeLike(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>SQLite stores the timestamp as ISO-8601 TEXT and the level as INTEGER; SQL Server as
    /// DATETIME2 and TINYINT, so both are read through their boxed value.</summary>
    public static LogEntry Read(DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        TimestampUtc = reader.GetValue(1) is DateTime timestamp
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
            : DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
        Level = (LogSeverity)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
        Application = reader.GetString(3),
        Category = reader.IsDBNull(4) ? null : reader.GetString(4),
        Message = reader.GetString(5),
        MessageTemplate = reader.IsDBNull(6) ? null : reader.GetString(6),
        Exception = reader.IsDBNull(7) ? null : reader.GetString(7),
        PropertiesJson = reader.IsDBNull(8) ? null : reader.GetString(8),
        CorrelationId = reader.IsDBNull(9) ? null : reader.GetString(9),
        MachineName = reader.IsDBNull(10) ? null : reader.GetString(10),
    };
}
