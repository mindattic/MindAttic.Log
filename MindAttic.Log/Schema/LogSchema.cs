namespace MindAttic.Log.Schema;

/// <summary>
/// The one table definition every sink targets. Column names and order are identical across SQL
/// Server and SQLite so a Reader query (or a human with a SQL client) never has to special-case
/// which backend it's pointed at. Table name is <c>MindAttic_Log</c> (underscore, not a literal
/// dot) purely so no query anywhere has to bracket-quote an identifier; the product name is still
/// "MindAttic.Log".
/// </summary>
public static class LogSchema
{
    public const string TableName = "MindAttic_Log";

    public static readonly string[] Columns =
    [
        "Id", "TimestampUtc", "Level", "Application", "Category", "Message",
        "MessageTemplate", "Exception", "PropertiesJson", "CorrelationId", "MachineName",
    ];

    /// <summary>
    /// SQLite DDL. Used both for a dedicated <c>MindAttic.Log.&lt;period&gt;.db</c> file (the
    /// no-database tier) and for the table inside an app's own SQLite database (the SQLite-backed
    /// tier) — same statement either way, see docs/BIBLE.md §4.
    /// </summary>
    public const string CreateTableSqlite = $"""
        CREATE TABLE IF NOT EXISTS {TableName} (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            TimestampUtc    TEXT    NOT NULL,
            Level           INTEGER NOT NULL,
            Application     TEXT    NOT NULL,
            Category        TEXT    NULL,
            Message         TEXT    NOT NULL,
            MessageTemplate TEXT    NULL,
            Exception       TEXT    NULL,
            PropertiesJson  TEXT    NULL,
            CorrelationId   TEXT    NULL,
            MachineName     TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS IX_{TableName}_TimestampUtc ON {TableName}(TimestampUtc);
        CREATE INDEX IF NOT EXISTS IX_{TableName}_Level ON {TableName}(Level);
        CREATE INDEX IF NOT EXISTS IX_{TableName}_Application ON {TableName}(Application);
        CREATE INDEX IF NOT EXISTS IX_{TableName}_CorrelationId ON {TableName}(CorrelationId);
        """;

    /// <summary>SQL Server DDL — same columns/types, SQL Server syntax and clustered index choice.</summary>
    public const string CreateTableSqlServer = $"""
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{TableName}')
        BEGIN
            CREATE TABLE dbo.{TableName} (
                Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                TimestampUtc    DATETIME2(3)   NOT NULL,
                Level           TINYINT        NOT NULL,
                Application     NVARCHAR(200)  NOT NULL,
                Category        NVARCHAR(400)  NULL,
                Message         NVARCHAR(MAX)  NOT NULL,
                MessageTemplate NVARCHAR(MAX)  NULL,
                Exception       NVARCHAR(MAX)  NULL,
                PropertiesJson  NVARCHAR(MAX)  NULL,
                CorrelationId   NVARCHAR(100)  NULL,
                MachineName     NVARCHAR(200)  NULL
            );
            CREATE INDEX IX_{TableName}_TimestampUtc ON dbo.{TableName}(TimestampUtc);
            CREATE INDEX IX_{TableName}_Level ON dbo.{TableName}(Level);
            CREATE INDEX IX_{TableName}_Application ON dbo.{TableName}(Application);
            CREATE INDEX IX_{TableName}_CorrelationId ON dbo.{TableName}(CorrelationId);
        END
        """;
}
