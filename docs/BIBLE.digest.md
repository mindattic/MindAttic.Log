---
codex: 1
project: MindAttic.Log
code: LOG
layer: digest
status: generated
generatedFrom: LOG-§1,LOG-§3,LOG-§5,LOG-§9
updated: 2026-10-14
---

# MindAttic.Log — BIBLE digest
AUTHORITATIVE — full detail in docs/BIBLE.md

## The one sentence
MindAttic.Log is the one logging pipeline for every MindAttic app: a strict shared schema
(`LogEntry`/`MindAttic_Log`) that every app writes through `ILogger<T>` as it already does, landing
in that app's own standalone `MindAttic.Log.db` SQLite database — one file per app, located via
`MindAttic.Vault`'s `VaultPaths.LoggingDbPath`, never shared with the app's own SQL Server or SQLite
store — plus a WPF + WebView2 Reader app that discovers and browses every app's database.

### 1.1 Superseded decision — the three-tier model (2026-10-08 → 2026-10-14) {#LOG-§1.1}
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

## What it is NOT
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
  before calling `AddMindAtticLog` (LOG-LAW-2). This library has no dependency on `MindAttic.Vault`.
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

## The Laws
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

### {#LOG-LAW-2} This library resolves no Vault locations itself.
`MindAtticLogOptions.DbPath` is a plain string the host supplies; `MindAttic.Log` never reads
`%APPDATA%`, Key Vault, or any other config/secret source itself, and has no package dependency on
`MindAttic.Vault`. The host computes `DbPath` via `VaultPaths.LoggingDbPath(appId)` before calling
`AddMindAtticLog`. (`MindAttic.Log/MindAtticLogOptions.cs`.) `MindAtticLogMigrator`'s source
connection info follows the same rule: the caller already has it, the migrator never resolves it.

### {#LOG-LAW-3} A logging failure never becomes an application failure.
Every sink catches and swallows its own storage-layer exceptions (`SqliteException` in
`MindAtticSqliteSink.Flush` and `.PruneOldRows`); a full disk or a locked file must never throw out
of an `ILogger.LogInformation(...)` call site. (`MindAttic.Log/Sinks/MindAtticSqliteSink.cs`.)

### {#LOG-LAW-4} The Reader never writes.
`MindAttic.Log.Reader` opens SQLite connections in `SqliteOpenMode.ReadOnly` and issues `SELECT`
only. It is a viewer, not a second writer that could race the app's own sink.
(`MindAttic.Log.Reader/Services/LogQueryService.cs`.)

## Glossary


## Status index (from USER_STORIES.md)
- done: 16 | partial: 5 | planned: 3
