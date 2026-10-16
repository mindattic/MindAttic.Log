---
codex: 1
project: MindAttic.Log
code: M.LOGS
layer: bible
status: living
updated: 2026-10-14
---

# MindAttic.Log — Project Bible
> Single source of truth for what MindAttic.Log IS, is NOT, and the rules that keep it coherent.
> README.md says how to build/consume the package; this says how to think about the system.

## 1. The one sentence {#M.LOGS-§1}
MindAttic.Log is the one logging pipeline for every MindAttic app: a strict shared schema
(`LogEntry`/`MindAttic_Log`) that every app writes through `ILogger<T>` as it already does, landing
in that app's own standalone `MindAttic.Log.db` SQLite database — one file per app, located via
`MindAttic.Vault`'s `VaultPaths.LoggingDbPath`, never shared with the app's own SQL Server or SQLite
store — plus a WPF + WebView2 Reader app that discovers and browses every app's database.

### 1.1 Superseded decision — the three-tier model (2026-10-08 → 2026-10-14) {#M.LOGS-§1.1}
Through 2026-10-12 this bible described a *deliberate* three-tier model instead: each app's rows
landed on whichever storage it already had — its own SQL Server database, its own SQLite file, or
(only for apps with no database at all) a rolled SQLite file in that app's own `%LocalAppData%`.
That reasoning wasn't wrong for what it optimized for — zero extra moving parts, reuse of
infrastructure an app already operated — and it shipped correctly to ~10 apps (docs/MIGRATION.md),
including a live production process (Prose's Hub).

It is superseded, not corrected, by a new requirement: logs living inside an app's own database or
`%LocalAppData%` folder are exactly as easy for a user to lose as that app's own data — cleared as
"just cache," dropped with a reinstall, gone if the app's database is restored from an older backup.
A standalone file under Vault's shared roaming root is harder to lose by accident, and registering
every app's log location through one place (Vault) means the Reader can discover and list every
app's logs itself, instead of a human pointing it at a folder/file/connection string by hand. See
§4 for the resulting architecture and docs/MIGRATION.md for the Phase-2 per-app migration this
requires.

## 2. The product promise {#M.LOGS-§2}
- **One schema, one backend.** `MindAttic.Log/Models/LogEntry.cs` and
  `MindAttic.Log/Schema/LogSchema.cs` define the exact columns every app's database holds — the same
  shape everywhere, so a query written against one app's `MindAttic.Log.db` works against any other's
  unchanged.
- **No call-site change.** Every surveyed app already uses `ILogger<T>` (see docs/MIGRATION.md).
  `services.AddMindAtticLog(...)` wires a Serilog pipeline underneath; apps delete their ad hoc
  writers, not their logging calls.
- **Still queryable, never a flat file.** A flat text file with no index degrades into noise as it
  grows (Prose used to regex-parse daily text files just to answer "what errors happened
  yesterday" — see §4). `MindAttic.Log.db` is a real SQLite database: real indexes, real `WHERE`
  clauses, zero server.
- **Live-readable while being written.** The SQLite sink runs in WAL mode specifically so
  MindAttic.Log.Reader can query a file while the app that owns it keeps logging to it.
- **A write must never take the host app down.** Every sink swallows its own storage failures
  (see `MindAtticSqliteSink.Flush`) rather than letting a full disk or a locked file propagate
  into application code that was just trying to log a warning.
- **Harder to lose than an app's own data.** The database lives under Vault's shared roaming root
  (`%APPDATA%\MindAttic\Logging\<appId>\`), not the app's own `%LocalAppData%` — clearing an app's
  local cache/data folder can never take its logs with it (see §1.1).

## 3. What it is NOT {#M.LOGS-§3}
- **NOT a log aggregation server.** No daemon, no ingestion API, no Seq/ELK-style central service.
  Every app still owns its own database file; MindAttic.Log.Reader reads those files directly, it
  does not proxy them. What changed from the superseded model (§1.1) is *where* that file lives
  (Vault's shared roaming root, not the app's own data folder) and that it's always the same kind of
  file (standalone SQLite) — not whether each app still has its own.
- **NOT a replacement for `ILogger<T>`.** Apps keep calling `ILogger<T>`/`Log.Information(...)`
  exactly as today; this package only supplies the sink underneath.
- **NOT a triage/issue tracker.** Prose's `LogIssue` table (signature-deduped Open/Resolved/Ignored
  rows, recomputed against live logs on every read — see §4) is a *derived* view over raw logs, not
  a copy of them, and is intentionally out of scope for v1. A future `MindAttic.Log.Issues` could
  build the same idea generically on top of `MindAttic_Log`, but nothing in this repo does it yet.
- **NOT a Blazor app.** The Reader is plain WPF + `Microsoft.Web.WebView2` with vanilla HTML/CSS/JS
  in `wwwroot/`, matching Automata.App and KdpPublish. BlazorWebView was considered and rejected —
  those two sibling apps already settled this for the ecosystem.
- **NOT a Vault client.** `MindAtticLogOptions.DbPath` is a plain string this library takes as-is and
  never resolves itself — the host app computes it via `MindAttic.Vault.Paths.VaultPaths.LoggingDbPath`
  before calling `AddMindAtticLog` (M.LOGS-LAW-2). This library has no dependency on `MindAttic.Vault`.
- **NOT a mandate to erase every other table or file that happens to hold records shaped like
  logs.** "One pipeline" means one pipeline for *general operational/diagnostic logging* — the
  thing `ILogger<T>`/`Log.Information(...)` calls produce. It does not mean folding a
  narrowly-scoped domain or compliance record into `MindAttic_Log` just because both are rows with
  a timestamp, and it does not mean a MindAttic.Log migration gets to restart a live production
  process on its own authority. Three concrete lines, each reached independently during the
  2026-10-08 ecosystem-wide migration pass (docs/MIGRATION.md) and confirmed here so a future pass
  doesn't re-litigate them as if they were oversights:
  - **Domain/compliance audit tables stay separate.** MindAttic.Authentication's `AuthAuditLog`
    (event type, outcome, hashed IP, user agent) and MediaButler's NDJSON file-mutation `AuditLog`
    (op/kind/from/to) are purpose-built records with their own schema and their own audience — a
    security reviewer, a file-recovery tool — not an operator reading application logs. Folding
    either into `MindAttic_Log`'s generic schema would be a schema regression for its real
    consumer, not a consolidation win. Two independent migrations reached this same conclusion.
  - **A live production process is never restarted by a logging migration on its own authority.**
    Prose's additive `MindAttic_Log` sink (docs/MIGRATION.md) was initially committed and pushed
    with the already-running Hub process left untouched. It was redeployed and restarted only
    after being asked and getting explicit confirmation, via the Hub's own official mechanism
    (`deploy-hub.bat`), not bounced ad hoc — the same process any other code change to Prose would
    go through.
  - **Interactive UI output is not a log, even when it goes to a console.** MindAttic.Launcher's
    `AnsiConsole.MarkupLine` calls across its 17 TUI files, and MindAttic.Deploy's equivalent, are
    the menu/progress text a human is reading in real time — the product, not a diagnostic trail
    of it. Logging infrastructure gets the crash/error path (which Launcher now has); it does not
    get to re-route an app's own UI through a database as a side effect of "one pipeline."
  - **A pre-existing store that backs a live, user-facing feature is not removed just because a
    sink now runs alongside it.** Tutor's `LogStorageService` still writes `app-logs.json`
    alongside `MindAtticLogBridge` (not instead of it) specifically because that file is what lets
    Tutor's in-app log viewer show history across an app restart *today*; deleting it would be a
    visible product behavior change (the viewer starts empty after every restart until new entries
    accumulate), not a logging-infrastructure decision, and nothing in this migration had the
    authority or the product context to make that call unilaterally.
  - **A Phase-2 per-app migration (§1.1) may drop the old `MindAttic_Log` table it just copied —
    but only with the same per-app explicit confirmation Prose's restart used above, never
    automatically.** `MindAtticLogMigrator`'s `dropSource` parameter defaults to `false`; a caller
    opts in per invocation. This is not a reversal of "never alter a live store unilaterally" — it's
    the same rule applied: the *authority* comes from asking each time, not from the migration code
    deciding for itself.

## 4. Architecture canon {#M.LOGS-§4}

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

### 4.2 Why SQLite, not text, and why standalone, not shared
Researched explicitly for this decision (not assumed): SQLite as a queryable, dependency-free log
store is validated prior art, not a novel idea here —
[Blacklite](https://tersesystems.com/blog/2020/11/26/queryable-logging-with-blacklite) (a JVM
diagnostic appender) reports SQL queries beating flat-file parsing and a vacuumed SQLite database
staying only slightly larger than the equivalent text log;
[logdive](https://docs.rs/crate/logdive/0.3.1) indexes JSON/logfmt/plain-text logs into local
SQLite with no daemon; and the general guidance from that research — promote the columns you
actually filter on (timestamp, level, application, category) into real indexed columns, keep WAL on
for concurrent readers — is exactly what `MindAttic.Log/Schema/LogSchema.cs` and
`MindAttic.Log/Sinks/MindAtticSqliteSink.cs` do.

The file is never rolled or replaced (unlike the superseded model's monthly `MindAttic.Log.<yyyy-MM>.db`
rotation, §1.1) — it's one `MindAttic.Log.db` for the life of the app. Retention is therefore
row-based: `MindAtticSqliteSink` sweeps rows older than `RetainedDays` (default 365) on its own
timer and runs `VACUUM` to reclaim the freed space, replacing the old model's file-count pruning.

The location — `%APPDATA%\MindAttic\Logging\<appId>\MindAttic.Log.db`, via
`MindAttic.Vault.Paths.VaultPaths.LoggingDbPath(appId)` — is deliberately Vault's shared **roaming**
root, not the app's own `%LocalAppData%`, per §1.1/§2. `Logging` is documented as a Vault bucket
(`../MindAttic.Vault/docs/BIBLE.md` §9) but is *not* a credential bucket: no `providers.json`, no
`IConfiguration` projection — just a shared, conventionally-named folder.

### 4.3 Sink architecture
```
                         ILogger<T> (unchanged call sites)
                                    |
                    services.AddMindAtticLog(options)  (ServiceCollectionExtensions)
                       options.DbPath = VaultPaths.LoggingDbPath(appId)   <- host resolves, not this library
                                    |
                         LoggerConfiguration + Serilog bridge
                                    |
                            MindAtticSqliteSink
                    (batched writes + WAL + its own row-based
                     retention timer; CREATE TABLE IF NOT EXISTS
                     on construction)
                                    |
                 %APPDATA%\MindAttic\Logging\<appId>\MindAttic.Log.db
```

`MindAttic_Log` is created by the sink itself (`CREATE TABLE IF NOT EXISTS`) on construction — there
is no separate migration/startup step, since every app's database is this library's own file, never
shared with anything else.

### 4.4 Migrating existing historical data
Apps already migrated onto the superseded three-tier model (docs/MIGRATION.md) have real
`MindAttic_Log` history sitting in a SQL Server table or inside their own app-owned SQLite file.
`MindAttic.Log/Migration/MindAtticLogMigrator.cs` is the one-shot tool each app's own Phase-2 pass
runs once: `MigrateFromSqlServerAsync`/`MigrateFromSqliteAsync` copy that app's rows into its new
standalone `MindAttic.Log.db` (idempotent by a per-`Application` `TimestampUtc` watermark, so a
re-run after a partial failure doesn't duplicate rows), and optionally drop the source table —
gated behind an explicit `dropSource: true` the caller must pass per invocation (see §3's confirmed
exception). This never runs automatically; see docs/MIGRATION.md for the per-app rollout.

### 4.5 Reader architecture
MindAttic.Log.Reader is a single-pane WPF `Microsoft.Web.WebView2` host (no AutoWebNav — that
package drives/records a *third-party* page under automation, which a read-only log viewer never
does). `LogQueryService` is the only thing that touches storage: `DiscoverApps()` lists every
subfolder of Vault's `Logging` bucket that has a `MindAttic.Log.db` (driving the app switcher in
`wwwroot/`), and `QueryAsync` opens a chosen app's database **read-only**. `MainWindow` is the
JS↔C# bridge (`WebMessageReceived` in, `ExecuteScriptAsync` out), exactly the pattern Automata.App
and KdpPublish already use.

## 5. The Laws {#M.LOGS-§5}
This project **inherits** the org-wide laws in
[`MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) by reference. Directly load-bearing:
- [HOUSE-LAW-1](../../MindAttic.HouseRules.md#HOUSE-LAW-1) — whole-number versioning.
- [HOUSE-LAW-3](../../MindAttic.HouseRules.md#HOUSE-LAW-3) — credentials resolve through
  MindAttic.Vault; see [M.LOGS-LAW-2](#M.LOGS-LAW-2).
- [HOUSE-LAW-6](../../MindAttic.HouseRules.md#HOUSE-LAW-6) — one engine, many front doors (every
  destination goes through the same `AddMindAtticLog`/`WriteToMindAtticLog` path).
- [HOUSE-LAW-8](../../MindAttic.HouseRules.md#HOUSE-LAW-8) — done is verified, not asserted.
- [HOUSE-LAW-10](../../MindAttic.HouseRules.md#HOUSE-LAW-10) — commit directly to main.

Project-specific laws:

### {#M.LOGS-LAW-1} One schema, no per-app drift.
No consumer app adds its own columns to `MindAttic_Log` or writes to it outside this library's
sinks. A need for an app-specific field is a conversation about extending `LogSchema` for
everyone (see `PropertiesJson` for the escape hatch that exists precisely so this law doesn't get
broken under pressure), not a local fork of the table.

### {#M.LOGS-LAW-2} This library resolves no Vault locations itself.
`MindAtticLogOptions.DbPath` is a plain string the host supplies; `MindAttic.Log` never reads
`%APPDATA%`, Key Vault, or any other config/secret source itself, and has no package dependency on
`MindAttic.Vault`. The host computes `DbPath` via `VaultPaths.LoggingDbPath(appId)` before calling
`AddMindAtticLog`. (`MindAttic.Log/MindAtticLogOptions.cs`.) `MindAtticLogMigrator`'s source
connection info follows the same rule: the caller already has it, the migrator never resolves it.

### {#M.LOGS-LAW-3} A logging failure never becomes an application failure.
Every sink catches and swallows its own storage-layer exceptions (`SqliteException` in
`MindAtticSqliteSink.Flush` and `.PruneOldRows`); a full disk or a locked file must never throw out
of an `ILogger.LogInformation(...)` call site. (`MindAttic.Log/Sinks/MindAtticSqliteSink.cs`.)

### {#M.LOGS-LAW-4} The Reader never writes.
`MindAttic.Log.Reader` opens SQLite connections in `SqliteOpenMode.ReadOnly` and issues `SELECT`
only. It is a viewer, not a second writer that could race the app's own sink.
(`MindAttic.Log.Reader/Services/LogQueryService.cs`.)

## 6. Glossary {#M.LOGS-§6}
- **Standalone database** — an app's one `MindAttic.Log.db`, the only tier (see §1.1 for the
  superseded SQL Server / app-owned-SQLite / rolled-file model).
- **Wire schema** — the column set in `MindAttic.Log/Schema/LogSchema.cs`.
- **`Logging` bucket** — the Vault roaming-root folder (`%APPDATA%\MindAttic\Logging\`) every app's
  standalone database lives under, one subfolder per app id; see `VaultPaths.LoggingDbPath`.
- **Sink** — `MindAtticSqliteSink`, the Serilog `ILogEventSink` that persists a `LogEntry` row.
- **Migrator** — `MindAtticLogMigrator`, the one-shot Phase-2 tool that copies an app's historical
  rows out of the superseded tiers (see §4.4, docs/MIGRATION.md).
- **Reader** — `MindAttic.Log.Reader`, the WPF + WebView2 viewer; discovers apps via the `Logging`
  bucket instead of a manual folder/file/connection-string picker.
