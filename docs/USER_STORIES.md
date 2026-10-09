---
codex: 1
project: MindAttic.Log
code: LOG
layer: stories
status: living
updated: 2026-10-08
---

# MindAttic.Log — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test that proves it.
> "Consumer" = a MindAttic app taking a dependency on the `MindAttic.Log` package.
> Verified 2026-10-08: `dotnet test MindAttic.Log.slnx` → Failed: 0, Passed: 9, Total: 9 (exit 0).
> Also verified end to end against a real consumer: `dotnet test Automata.Tests.csproj` → 571/571
> passing, including `MindAtticLogIntegrationTests` (Automata repo) which writes a log row through
> `AddMindAtticLog` and reads it back out of the same `automata.db` file EF owns.

## Epic A — The shared schema

- **LOG-US-A1 ✅** As a consumer, the SQLite tier's `CREATE TABLE` statement defines exactly the
  columns `LogSchema.Columns` declares, so the schema can't drift between the constant list and
  the DDL that actually runs. *(verified by `Columns_Cover_Every_Column_In_Sqlite_Ddl`.)*
- **LOG-US-A2 ✅** As a consumer, both the SQL Server and SQLite DDL target the same table name
  (`MindAttic_Log`), so a query written for one backend needs only its table prefix changed to run
  on the other. *(verified by `Sqlite_And_SqlServer_Ddl_Reference_Same_Table`.)*
- **LOG-US-A3 ✅** As a consumer, writing a log event through `MindAtticSqliteSink` produces a row
  with the level, application, rendered message, raw message template, and structured properties
  all readable back via plain SQL — proving the sink and the schema agree with each other, not
  just with themselves. *(verified by `Emit_Then_Dispose_Flushes_A_Readable_Row`.)*
- **LOG-US-A4 ✅** As a consumer with no existing database file, constructing the sink creates one
  with the table already in it, so there's no separate migration step for the no-database tier.
  *(verified by `Constructor_Creates_Table_Even_With_No_Events_Emitted`.)*

## Epic B — The no-database (rolled SQLite) tier

- **LOG-US-B1 ✅** As a consumer with no database, the rolled file path embeds the year and month
  it was written in, so a Reader can skip files outside a requested date range without opening
  them. *(verified by `CurrentPath_Embeds_Year_And_Month`.)*
- **LOG-US-B2 ✅** As a consumer, retention keeps only the N most recently-named rolled files and
  deletes the rest, so disk usage for the no-database tier is bounded. *(verified by
  `PruneOldFiles_Keeps_Only_The_Most_Recent_N`.)*
- **LOG-US-B3 ✅** As a consumer who sets retention to zero, no files are deleted (zero means "no
  limit", not "delete everything"). *(verified by `PruneOldFiles_With_Zero_Retention_Deletes_Nothing`.)*

## Epic C — DI integration

- **LOG-US-C1 ✅** As a consumer, `services.AddMindAtticLog(o => ...)` wires a Serilog pipeline
  under `ILogger<T>` with no call-site changes, and `ILogger<T>` actually resolves from the
  container. *(verified by `AddMindAtticLog_Makes_ILogger_Of_T_Resolvable`.)* An earlier version
  called `services.AddSerilog(...)` directly, which registers the Serilog bridge but not the open
  generic `ILogger<T>`/`ILoggerFactory` services — caught by Automata's own integration test before
  this story was marked done; fixed by routing through `services.AddLogging(builder => ...)`
  instead. See LOG-US-C2.
- **LOG-US-C2 ✅** As the first real consumer (Automata — see docs/MIGRATION.md), wiring
  `AddMindAtticLog` into `AddAutomataCore` with `Destination = LogDestination.Sqlite` pointed at
  Automata's own `automata.db` produces a working `MindAttic_Log` table inside that same file,
  coexisting with EF's own tables, reachable through the `ILogger<T>` calls `CollectionStore` /
  `WorkflowEngine` / etc. already make. *(verified by Automata repo's
  `MindAtticLogIntegrationTests.AddMindAtticLog_Writes_Into_The_Same_File_As_Automata_Own_Tables`;
  full Automata suite — 571/571 — still green after the change.)*
- **LOG-US-C3 ✅** As a consumer with a non-`ILogger<T>` logging call-site pattern (Tutor's own
  static `Log`/`LogStore` facade), the sink can still be driven directly — not every app needs to
  go through `AddMindAtticLog`/DI at all. *(verified by Tutor repo's `MindAtticLogBridgeTests`:
  `Log_Error_Reaches_The_Shared_MindAttic_Log_Table` and
  `Existing_LogStore_Still_Receives_Entries_Alongside_The_Bridge`; full Tutor suite — 456/456 —
  still green after the change.)*

## Epic D — The SQL Server tier

- **LOG-US-D1 🟡** As a consumer with SQL Server, `WriteToMindAtticLog(LogDestination.SqlServer)`
  configures `Serilog.Sinks.MSSqlServer` with `AutoCreateSqlTable = false` and additional columns
  mapped onto `LogSchema`'s names. Compiles clean; not yet exercised against a real SQL Server
  instance (no CI database available at the time this was written).

## Epic E — MindAttic.Log.Reader

- **LOG-US-E1 🟡** As an operator, I can open a folder of rolled `MindAttic.Log.*.db` files, filter
  by date/level/application/category/text, and see matching rows. The WPF+WebView2 shell and
  `LogQueryService` build clean (`dotnet build MindAttic.Log.Reader`); not yet manually verified
  against a real log file in a running window (no interactive Windows session available at the
  time this was written).
- **LOG-US-E2 ⬜** As an operator, I can connect to a SQL Server `MindAttic_Log` table the same way.
  Query path exists (`LogQueryService.QuerySqlServerAsync`); unverified end to end.
- **LOG-US-E3 ⬜** As an operator, SQL Server connection strings resolve through MindAttic.Vault by
  name rather than being typed in plaintext (`LOG-LAW-2`). Not yet built — v1's `sqlServerInput`
  box takes a raw string for the session only.
