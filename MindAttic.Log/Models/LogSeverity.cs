namespace MindAttic.Log.Models;

/// <summary>
/// Matches Serilog's <c>LogEventLevel</c> ordinal order so mapping either direction is a cast,
/// not a lookup table.
/// </summary>
public enum LogSeverity
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5,
}
