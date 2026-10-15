---
codex: 1
project: MindAttic.Log
code: LOG
layer: stories
status: living
updated: 2026-10-14
---

# MindAttic.Log — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test that proves it.
> "Consumer" = a MindAttic app taking a dependency on the `MindAttic.Log` package.
> Verified 2026-10-14: `dotnet test MindAttic.Log.slnx` → Failed: 0, Passed: 12, Total: 12 (exit 0),
> following the 2026-10-14 single-tier rewrite (docs/BIBLE.md §1.1). Epics B and D's older
> entries (`AddMindAtticLog_Makes_ILogger_Of_T_Resolvable` aside) describe the superseded/retired
> rolled-file and SQL-Server-write tiers and are kept as historical record, not current behavior.
> Each already-migrated consumer app (Automata, Ideas, Prose, Tutor, …) needs its own Phase-2 pass
> (docs/MIGRATION.md) onto the new standalone database before its own entries here are current
> again.

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

## Epic B — Standalone database & row-based retention
> **Superseded 2026-10-14** (see docs/BIBLE.md §1.1): this epic used to be "the no-database
> (rolled SQLite) tier" — B1/B2/B3 below described `LogFileRoller`'s monthly-file rolling and
> file-count retention, which no longer exist now that every app (not just the no-database ones)
> gets one standalone, never-rolled `MindAttic.Log.db`. Rewritten in place rather than left to rot,
> since the underlying feature is gone, not renamed.

- **LOG-US-B1 ✅** As a consumer, every app's database is one standalone, Vault-located
  `MindAttic.Log.db` — never rolled, never shared with the app's own store — resolved via
  `VaultPaths.LoggingDbPath(appId)` and passed in as `MindAtticLogOptions.DbPath`. *(verified by
  `AddMindAtticLog_Makes_ILogger_Of_T_Resolvable`, Vault's own `LoggingDbPath_Combines_Roaming_Root_Bucket_App_And_FileName`.)*
- **LOG-US-B2 ✅** As a consumer, a background sweep deletes rows older than `RetainedDays` and
  reclaims the freed space, so disk usage stays bounded without ever rolling or replacing the file.
  *(verified by `PruneOldRows_Deletes_Rows_Older_Than_RetainedDays_But_Keeps_Newer_Ones`.)*
- **LOG-US-B3 ✅** As a consumer migrating off the retired SQL Server/app-owned-SQLite tiers,
  `MindAtticLogMigrator` copies that app's historical rows into its new standalone database
  idempotently (a re-run after a partial failure doesn't duplicate rows) and only drops the source
  table when explicitly told to. *(verified by `MigrateFromSqliteAsync_Copies_All_Rows_Into_Destination`,
  `MigrateFromSqliteAsync_Rerun_Does_Not_Duplicate_Already_Copied_Rows`,
  `MigrateFromSqliteAsync_With_DropSource_Removes_The_Source_Table`,
  `MigrateFromSqliteAsync_Without_DropSource_Leaves_The_Source_Table_Intact`; the SQL Server source
  path against a real throwaway LocalDB database by
  `MigrateFromSqlServerAsync_Copies_Rows_Into_Destination_And_Drops_Source_When_Asked`,
  `[Explicit]` same convention as this ecosystem's other live-SQL-Server tests.)* That LocalDB run
  caught a real bug: an untyped `DateTime` watermark parameter was inferred as the legacy SQL
  `datetime` type (range 1753–9999) instead of the column's own `datetime2`, so
  `DateTime.MinValue` (the no-prior-migration watermark) overflowed the *parameter* on the very
  first run — fixed by pinning `SqlDbType.DateTime2` explicitly.

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
> **Superseded 2026-10-14** (see docs/BIBLE.md §1.1): `LogDestination.SqlServer`/
> `WriteToMindAtticLog(LogDestination.SqlServer)`/`MindAtticLogEnricher` are deleted — the library
> no longer writes to SQL Server at all. D1 below is kept as the historical record of what shipped
> and was verified at the time; SQL Server now appears only as a one-shot *read* source for
> `MindAtticLogMigrator` (Epic B, LOG-US-B3), never an ongoing write tier.

- **LOG-US-D1 ✅ (retired)** As a consumer with SQL Server, `WriteToMindAtticLog(LogDestination.SqlServer)`
  configures `Serilog.Sinks.MSSqlServer` with `AutoCreateSqlTable = false` and additional columns
  mapped onto `LogSchema`'s names, and a row written through `ILogger<T>` actually lands and reads
  back correctly. *(verified by Ideas repo's `MindAtticLogSqlServerTests` —
  `AddMindAtticLog_SqlServer_Writes_A_Readable_Row_Through_ILogger` — against a throwaway SQL
  Server LocalDB database the test provisions and drops itself; `[Explicit]`, same convention as
  that repo's other live-SQL-Server tests.)* This run caught a real bug:
  `Level.StoreAsEnum` defaulted to `false` (stores the level as text, e.g. "Warning"), which doesn't
  fit `LogSchema`'s `TINYINT` column — fixed to `true` (Serilog's `LogEventLevel` is numbered
  identically to `Models.LogSeverity`, so both backends now store the same integer).

## Epic F — Ecosystem-wide pass

- **LOG-US-F1 🟡** As a consumer with no test project of its own (Mobile, Formicarium,
  MindAttic.Vault.Dashboard), the pipeline can still be verified without inventing test
  infrastructure uninvited: run the real built app and confirm actual rows land. Mobile: ran the
  built exe, confirmed log lines reached the rolled `.db` file. Formicarium: ran the Dashboard
  against its dev LocalDB, confirmed 20 real rows in `dbo.MindAttic_Log`. Vault.Dashboard: ran the
  built app for 8 seconds, confirmed 61 real rows in the rolled file, including the background
  monitor's own startup line. Verified live, not by a named automated test — no test token to
  cite, honestly partial by this file's own convention (see docs/MIGRATION.md for the full account).
- **LOG-US-F2 ✅** As a consumer with zero prior crash visibility (MediaButler's WPF front door had
  no `DispatcherUnhandledException`/`AppDomain.UnhandledException` handlers at all before this),
  the rolled-file tier can back a purpose-built crash log wired into newly-added handlers, not just
  an existing logging call site. *(verified by MediaButler repo's `CrashLogTests`; full suite
  298/298 still green.)*
- **LOG-US-F3 ✅** As the library's own design, a narrowly-scoped domain-specific record table
  (MindAttic.Authentication's `AuthAuditLog`; MediaButler's NDJSON file-mutation `AuditLog`) is
  correctly never folded into `MindAttic_Log` — confirmed independently by two separate migrations
  reaching the same conclusion for different reasons, not just asserted once. See LOG-LAW-1.
- **LOG-US-F4 🟡** As the library's own design, an app with no real integration point (a stateless
  library, a pure-TUI tool whose "logging" is actually its interactive UI, a non-.NET script, an
  archived repo) is correctly left untouched rather than having a sink forced onto it. Confirmed
  across ten repos in a single ecosystem-wide pass (MindAttic.Deploy, Export, Helpers, Bob,
  Cryptography, Ideas.Library, Media, Psst, Web, ChiMesh) — see docs/MIGRATION.md for each one's
  specific reasoning. No test token: this story is a design judgment confirmed by absence of
  unnecessary code, not something an automated test asserts.

## Epic G — High-stakes and no-DI-container consumers

- **LOG-US-G1 ✅ (historical — Phase-1 only)** As the reference app itself (Prose, a live production system with a 14-day
  Serilog file pipeline and `LogIssue` triage already relied on), the shared pipeline can be
  adopted additively — a second `builder.Logging.AddSerilog(...)` provider alongside the existing
  ones, writing `MindAttic_Log` into the same database, with nothing existing touched, rerouted,
  or removed. *(verified by Prose repo's `MindAtticLogSqlServerTests` —
  `AddMindAtticLog_SqlServer_Writes_A_Readable_Row_Through_ILogger` — against a throwaway LocalDB
  database; full suite 3007/3026 unchanged before/after. The live Hub process was subsequently
  redeployed and restarted — with explicit confirmation requested and given first — via its own
  official mechanism; a direct query against the real `Prose` database confirmed genuine startup
  diagnostics landing within seconds of the restart.)* The SQL Server write path this depended on is
  retired (see Epic D); Prose needs its own Phase-2 pass (docs/MIGRATION.md) onto a standalone
  `MindAttic.Log.db`, via `MindAtticLogMigrator`, before this story is current again.
- **LOG-US-G2 🟡** As a consumer with no DI container at all (MindAttic.Launcher — Spectre.Console.Cli
  constructs commands directly, no `IServiceCollection`), the pipeline can still be wired by
  calling `WriteToMindAtticLog` directly on a `LoggerConfiguration` at the entry point, with a
  top-level try/catch giving the app its first crash visibility ever. Verified live: ran the built
  exe, confirmed the rolled file and its table are created cleanly — no automated test, no token
  to cite, honestly partial (same live-verification class as LOG-US-F1).

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

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| LOG-T1 | 2026-10-12 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
