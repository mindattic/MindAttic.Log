---
codex: 1
project: MindAttic.Log
code: LOG
layer: digest
status: generated
generatedFrom: LOG-§1,LOG-§3,LOG-§5,LOG-§9
updated: 2026-10-08
---

# MindAttic.Log — BIBLE digest
AUTHORITATIVE — full detail in docs/BIBLE.md

## The one sentence
MindAttic.Log is the one logging pipeline for every MindAttic app: a strict shared schema
(`LogEntry`/`MindAttic_Log`) that every app writes through `ILogger<T>` as it already does, landing
on whichever storage the app already has — a `MindAttic_Log` table on SQL Server, the same table
inside the app's own SQLite database, or a dedicated rolling SQLite file for apps with no database
at all — plus a WPF + WebView2 Reader app to browse all three.

## What it is NOT
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

## Glossary


## Status index (from USER_STORIES.md)
- done: 13 | partial: 2 | planned: 3
