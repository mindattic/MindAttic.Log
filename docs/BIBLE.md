---
codex: 1
project: MindAttic.Log
code: LOG
layer: bible
status: living
updated: 2026-10-08
---

# MindAttic.Log — Project Bible
> Single source of truth for what MindAttic.Log IS, is NOT, and the rules that keep it coherent.
> README.md says how to build/consume the package; this says how to think about the system.

## 1. The one sentence {#LOG-§1}
MindAttic.Log is the one logging pipeline for every MindAttic app: a strict shared schema
(`LogEntry`/`MindAttic_Log`) that every app writes through `ILogger<T>` as it already does, landing
on whichever storage the app already has — a `MindAttic_Log` table on SQL Server, the same table
inside the app's own SQLite database, or a dedicated rolling SQLite file for apps with no database
at all — plus a WPF + WebView2 Reader app to browse all three.

## 2. The product promise {#LOG-§2}
- **One schema, every backend.** `MindAttic.Log/Models/LogEntry.cs` and `MindAttic.Log/Schema/LogSchema.cs` define the exact
  same columns whether the sink is SQL Server or SQLite. A query written for one tier works on the
  other with only the table prefix changed.
- **No call-site change.** Every surveyed app already uses `ILogger<T>` (see docs/MIGRATION.md).
  `services.AddMindAtticLog(...)` wires a Serilog pipeline underneath; apps delete their ad hoc
  writers, not their logging calls.
- **The "file" tier is still queryable.** A flat text file with no index degrades into noise as it
  grows (Prose currently regex-parses daily text files just to answer "what errors happened
  yesterday" — see §4). The no-database tier is a rolled SQLite file, not text: one portable file
  per period, real indexes, real `WHERE` clauses, zero server.
- **Live-readable while being written.** The SQLite sink runs in WAL mode specifically so
  MindAttic.Log.Reader can query a file while the app that owns it keeps logging to it.
- **A write must never take the host app down.** Every sink swallows its own storage failures
  (see `MindAtticSqliteSink.Flush`) rather than letting a full disk or a locked file propagate
  into application code that was just trying to log a warning.

## 3. What it is NOT {#LOG-§3}
- **NOT a log aggregation server.** No daemon, no ingestion API, no Seq/ELK-style central service.
  Every app owns its own storage (its SQL Server database, its SQLite file, or its own directory of
  rolled files); MindAttic.Log.Reader reads those directly, it does not proxy them.
  Per-app ownership was deliberate — see `docs/MIGRATION.md` for which apps use which tier.
- **NOT a replacement for `ILogger<T>`.** Apps keep calling `ILogger<T>`/`Log.Information(...)`
  exactly as today; this package only supplies the sink underneath.
- **NOT a triage/issue tracker.** Prose's `LogIssue` table (signature-deduped Open/Resolved/Ignored
  rows, recomputed against live logs on every read — see §4) is a *derived* view over raw logs, not
  a copy of them, and is intentionally out of scope for v1. A future `MindAttic.Log.Issues` could
  build the same idea generically on top of `MindAttic_Log`, but nothing in this repo does it yet.
- **NOT a Blazor app.** The Reader is plain WPF + `Microsoft.Web.WebView2` with vanilla HTML/CSS/JS
  in `wwwroot/`, matching Automata.App and KdpPublish. BlazorWebView was considered and rejected —
  those two sibling apps already settled this for the ecosystem.
- **NOT a credential resolver.** SQL Server connection strings are credentials; this library takes
  one as a plain string (`MindAtticLogOptions.SqlServerConnectionString`) and never resolves it
  itself — the host app resolves it through MindAttic.Vault per HOUSE-LAW-3 before calling in.

## 4. Architecture canon {#LOG-§4}

### 4.1 Why this schema
Derived from auditing Prose's actual logging (not a guess at "industry best practice" — see the
repo-wide survey this was built from, folded into docs/MIGRATION.md):

- Prose's durable tier is daily Serilog text files (`log-yyyyMMdd.txt`, 14-day retention), parsed
  back with the regex `^(.+?) \[(\w{3})\] (.+)$` into `{Timestamp, Level, Message, Exception}`.
  Its live tier (an in-memory ring buffer) additionally carries `Category` — a field the *file*
  tier silently drops, because Serilog's default text template never prints `SourceContext`.
- Prose's `LogIssue` table is a separate, derived triage layer (signature/title/status), not a copy
  of raw log rows — evidence that "logs" and "the queryable index over logs" are different concerns,
  which is exactly the split this repo keeps: `MindAttic_Log` is raw rows; a future issues table
  would be derived from it, not the other way around.

`LogEntry` is Prose's three tiers reconciled into one row shape: `TimestampUtc`, `Level`,
`Application` (new — Prose is single-app, MindAttic.Log is not), `Category` (fixes the file-tier
gap), `Message`, `MessageTemplate` (new — lets a Reader group by shape, not exact text),
`Exception`, `PropertiesJson` (new — keeps the column count fixed regardless of what a call site
logs), `CorrelationId` (new — cross-call tracing), `MachineName`.

### 4.2 Why the "file" tier is SQLite, not text
Researched explicitly for this decision (not assumed): SQLite as a queryable, dependency-free log
store is validated prior art, not a novel idea here —
[Blacklite](https://tersesystems.com/blog/2020/11/26/queryable-logging-with-blacklite) (a JVM
diagnostic appender) reports SQL queries beating flat-file parsing and a vacuumed SQLite database
staying only slightly larger than the equivalent text log;
[logdive](https://docs.rs/crate/logdive/0.3.1) indexes JSON/logfmt/plain-text logs into local
SQLite with no daemon; and the general guidance from that research — promote the columns you
actually filter on (timestamp, level, application, category) into real indexed columns, keep WAL
on for concurrent readers, roll/retain by file rather than by row — is exactly what
`MindAttic.Log/Schema/LogSchema.cs`, `MindAttic.Log/Sinks/MindAtticSqliteSink.cs`, and `MindAttic.Log/Sinks/LogFileRoller.cs` do.

Rolling is monthly (`MindAttic.Log.<yyyy-MM>.db`), not daily like Prose's text files — a queryable
DB file stays useful well past a day, so daily rotation would just mean the Reader has to open more
files per query for no benefit. Retention is by file **count** (`RetainedFileCount`, default 12),
mirroring Prose's `retainedFileCountLimit` idea rather than inventing a different retention model.

### 4.3 Sink architecture
```
                         ILogger<T> (unchanged call sites)
                                    |
                    services.AddMindAtticLog(options)  (ServiceCollectionExtensions)
                                    |
                         LoggerConfiguration + Serilog bridge
                                    |
              +---------------------------------------+
              |                                        |
     Destination.SqlServer                     Destination.Sqlite
              |                                        |
   MindAtticLogEnricher projects            MindAtticSqliteSink
   Application/MachineName/                 (batched, WAL, own
   MessageTemplate/PropertiesJson            CREATE TABLE IF NOT
   onto the event as properties              EXISTS on construction)
              |                                        |
   Serilog.Sinks.MSSqlServer,                 dbo app's .db file, OR
   AdditionalColumns mapped to        LogFileRoller-computed MindAttic.Log.<yyyy-MM>.db
   those same property names                  when there's no app database
              |                                        |
     dbo.MindAttic_Log (SQL Server)            MindAttic_Log table (SQLite)

                    Both produce identical LogSchema.Columns rows.
```

`LogSchema.CreateTableSqlServer` is run once by the host (migration/startup step) — the SQL Server
sink never auto-creates the table, so the two backends can never silently drift from `LogSchema`.
The SQLite sink does create its own table on construction (`CREATE TABLE IF NOT EXISTS`) since a
rolled file or a fresh app database won't have one yet and there is no separate migration step for
file-based apps to run.

### 4.4 Reader architecture
MindAttic.Log.Reader is a single-pane WPF `Microsoft.Web.WebView2` host (no AutoWebNav — that
package drives/records a *third-party* page under automation, which a read-only log viewer never
does). `LogQueryService` is the only thing that touches storage: it opens SQLite connections
**read-only**, and for a folder source it skips any rolled file whose embedded `yyyy-MM` cannot
overlap the requested date range rather than opening every file present. `MainWindow` is the
JS↔C# bridge (`WebMessageReceived` in, `ExecuteScriptAsync` out), exactly the pattern Automata.App
and KdpPublish already use.

## 5. The Laws {#LOG-§5}
This project **inherits** the org-wide laws in
[`MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) by reference. Directly load-bearing:
- [HOUSE-LAW-1](../../MindAttic.HouseRules.md#HOUSE-LAW-1) — whole-number versioning.
- [HOUSE-LAW-3](../../MindAttic.HouseRules.md#HOUSE-LAW-3) — credentials resolve through
  MindAttic.Vault; see [LOG-LAW-2](#LOG-LAW-2).
- [HOUSE-LAW-6](../../MindAttic.HouseRules.md#HOUSE-LAW-6) — one engine, many front doors (every
  destination goes through the same `AddMindAtticLog`/`WriteToMindAtticLog` path).
- [HOUSE-LAW-8](../../MindAttic.HouseRules.md#HOUSE-LAW-8) — done is verified, not asserted.
- [HOUSE-LAW-10](../../MindAttic.HouseRules.md#HOUSE-LAW-10) — commit directly to main.

Project-specific laws:

### {#LOG-LAW-1} One schema, no per-app drift.
No consumer app adds its own columns to `MindAttic_Log` or writes to it outside this library's
sinks. A need for an app-specific field is a conversation about extending `LogSchema` for
everyone (see `PropertiesJson` for the escape hatch that exists precisely so this law doesn't get
broken under pressure), not a local fork of the table.

### {#LOG-LAW-2} This library resolves no credentials.
`MindAtticLogOptions.SqlServerConnectionString` is a plain string the host supplies; `MindAttic.Log`
never reads `%APPDATA%`, Key Vault, or any other secret source itself. The host resolves it through
MindAttic.Vault before calling `AddMindAtticLog`. (`MindAttic.Log/MindAtticLogOptions.cs`.)

### {#LOG-LAW-3} A logging failure never becomes an application failure.
Every sink catches and swallows its own storage-layer exceptions (`SqliteException` in
`MindAtticSqliteSink.Flush`); a full disk, a locked file, or an unreachable SQL Server must never
throw out of an `ILogger.LogInformation(...)` call site. (`MindAttic.Log/Sinks/MindAtticSqliteSink.cs`.)

### {#LOG-LAW-4} The Reader never writes.
`MindAttic.Log.Reader` opens SQLite connections in `SqliteOpenMode.ReadOnly` and issues `SELECT`
only, on both backends. It is a viewer, not a second writer that could race the app's own sink.
(`MindAttic.Log.Reader/Services/LogQueryService.cs`.)

## 6. Glossary {#LOG-§6}
- **Tier** — which of the three backends an app's `MindAtticLogOptions.Destination` targets:
  SQL Server, SQLite (app-owned `.db`), or SQLite (no-database, rolled file).
- **Wire schema** — the column set in `MindAttic.Log/Schema/LogSchema.cs`, identical across both backends.
- **Rolled file** — a `MindAttic.Log.<yyyy-MM>.db` produced by `LogFileRoller` for the
  no-database tier.
- **Sink** — the Serilog `ILogEventSink` (or `Serilog.Sinks.MSSqlServer` configuration) that
  actually persists a `LogEntry` row.
- **Reader** — `MindAttic.Log.Reader`, the WPF + WebView2 viewer.
