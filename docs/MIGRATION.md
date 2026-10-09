---
codex: 1
project: MindAttic.Log
code: LOG
layer: migration
status: living
updated: 2026-10-08
---

# MindAttic.Log — Migration tracker

> Tracks which MindAttic apps have been audited for existing logging and migrated onto the shared
> `MindAttic.Log` pipeline. Not a bible section — this is working state, expected to change on
> every migration PR. ✅ migrated · 🟡 audited, not yet migrated · ⬜ not yet looked at.
>
> Seeded 2026-10-08 from a repo-wide survey (read-only audit of every `MindAttic.*` repo plus
> Prose, Tutor, Automata, GridGame2026, ChiMesh, Formicarium, KdpPublish, MediaButler). See
> `docs/BIBLE.md` §4.1 for what the survey found about Prose's schema specifically.

## Legend

- **Tier** — `SqlServer`, `Sqlite` (app-owned `.db`), or `Sqlite` (no-database, rolled file) — see
  `docs/BIBLE.md` §4.1. The no-database tier is still SQLite, not plain text (§4.2).
- **Current logging** — what the survey found at each app as of 2026-10-08.

## Status

| Project | Stack | DB backend | Tier | Current logging (as surveyed) | Status |
|---|---|---|---|---|---|
| Prose | .NET | SQL Server | SqlServer | Serilog daily text files (`Prose.Hub/Program.cs`) parsed back by `LoggingService`; separate derived `LogIssue` triage table. Reference model for the schema — see BIBLE §4.1. | 🟡 |
| MindAttic.Authentication | .NET | Host-provided (SQL Server typical) | SqlServer | `ILogger<T>` + custom `AuthAuditWriter` → `AuthAuditLog` table (security-audit specific, not general app logging — likely stays separate from `MindAttic_Log`). | ⬜ |
| MindAttic.Ideas | .NET Blazor | SQL Server (`CmsDbContext`) | SqlServer | `ILogger` present, no durable sink found. | ⬜ |
| Tutor | .NET Blazor | SQL Server, but auth-only (`TutorAuthDbContext` is MindAttic.Authentication's identity schema; "course content and per-user progress stay JSON" per its own doc comment) | Sqlite (rolled file) | Migrated: re-checked 2026-10-08 and found Tutor has its own hand-rolled logging — a static `Log`/`LogStore` facade (`Log.Info`/`Warn`/`Error`/`Critical`, used throughout the app) persisted by `LogStorageService` as a single `app-logs.json` **rewritten whole on every save** — exactly the monolithic-file anti-pattern this repo's design rejected (see BIBLE §4.2). Added `MindAtticLogBridge`, which subscribes to `LogStore.EntryAdded` and forwards every entry into the rolled-SQLite tier — zero call-site changes, existing JSON persistence and live in-app viewer left untouched. TutorAuth's SQL Server DB was deliberately not used: it's a narrowly-scoped identity schema (same reasoning as MindAttic.Authentication's own `AuthAuditLog`), not a general-purpose app database. | ✅ |
| Automata | .NET + Node tools | SQLite (`AutomataDb`, EF Core) | Sqlite (app-owned) | Migrated: `AddAutomataCore` calls `AddMindAtticLog` pointed at `AutomataDatabase.ResolvePath()` — same file EF owns. `ILogger<T>` call sites (`WorkflowEngine`/`FlowAuthoringService`/`ReplayEngine`/etc.) are unchanged; only the sink underneath is new. | ✅ |
| MindAttic.Launcher | .NET (net10.0-windows) | none | Sqlite (rolled file) | Raw `Console.Write*`/`AnsiConsole.MarkupLine` across 17 files (Commands/, Menus/, Services/, Ui/) — **no structured logging at all**. First integration target: no existing pipeline to reconcile with. | ⬜ |
| KdpPublish | .NET (WPF) | none | — | Re-checked 2026-10-08: KdpPublish has no logging surface of its own. Its only own-code "logging" is a `#if DEBUG`-only `File.AppendAllText` crash dump in `App.xaml.cs` — deliberately dependency-free so it still works if a catastrophic crash breaks everything else (same reasoning Automata.App uses, see its own App.xaml.cs comment), not a candidate for routing through a DI-based sink. All of KdpPublish's real logging flows through vendored `Prose.Core` (`AddProseServices()`); migrating it is the same work as migrating Prose, not a separate no-DB target. | n/a — folded into Prose |
| MindAttic.Vault | .NET | none (settings store) | Sqlite (rolled file) | `ILogger<T>` in `AlertDispatcher`, `MonitorBackgroundService`; no durable sink. | ⬜ |
| MindAttic.Legion | .NET | none | Sqlite (rolled file) | `Microsoft.Extensions.Logging` referenced in DI extensions; no durable sink. | ⬜ |
| MindAttic.Deploy | .NET CLI + Node | none | Sqlite (rolled file) | No logging detected via grep — likely console-only. | ⬜ |
| MindAttic.Bob | — | none found | — | No app logging surface found (installer/CLI). | ⬜ |
| MindAttic.Cryptography | — | none | — | No csproj found at survey depth; minimal library footprint. | ⬜ |
| MindAttic.Export | .NET | none | Sqlite (rolled file) | No logging detected. | ⬜ |
| MindAttic.Helpers | .NET | none | Sqlite (rolled file) | No logging detected (shared helper library). | ⬜ |
| MindAttic.Ideas.Library | .NET | none | Sqlite (rolled file) | No logging detected (widget/theme library). | ⬜ |
| MindAttic.Media | .NET (+ Azure variant) | none found | — | No logging detected. | ⬜ |
| MindAttic.Mobile | .NET | none | Sqlite (rolled file) | No logging detected (WebSocket/xterm bridge). | ⬜ |
| MindAttic.Psst | .NET | none | Sqlite (rolled file) | No logging detected (notification tool). | ⬜ |
| MindAttic.Web | mixed Node + .NET | none | — | `console.log` in JS components; not a .NET consumer of this package. | ⬜ |
| Formicarium | .NET dashboard + firmware | none found | — | `ILogger`-style services (`ColonyMonitor`, `TelemetryStore`); no durable sink confirmed. | ⬜ |
| GridGame2026 | Unity/C# | n/a | — | Unity's own logging; not a service app — out of scope for this pipeline. | skip |
| ChiMesh | — | none | — | No logging detected (console/tools/scripts). | ⬜ |
| MediaButler | .NET | none | — | No logging detected. | ⬜ |

## Ecosystem pattern (from the survey)

Most .NET apps already call `ILogger<T>` at their call sites — **none wire a durable sink** except
Prose (Serilog file) and Authentication (a purpose-built security-audit table that should likely
stay separate from general `MindAttic_Log` rows rather than be folded in). Three backend shapes
exist in the wild, confirming the three-tier design: SQL Server (Ideas, Tutor, Authentication's
host), SQLite (Automata), no-DB (Launcher, Vault, Legion, Deploy, and most tool-shaped repos —
KdpPublish hand-rolls a file logger today). `ILogger<T>` being the near-universal call-site API
across the ecosystem is exactly why `services.AddMindAttic Log(...)` targets the DI/Serilog seam
instead of asking every app to change its logging calls.

## Suggested migration order

1. **Automata ✅ (2026-10-08)** — done first instead of Launcher: Launcher had uncommitted changes
   in exactly the files that would need touching, so Automata went first to prove the app-owned-
   SQLite tier without stepping on in-progress work. `MindAtticSqliteSink`'s `services.AddSerilog(...)`
   call turned out not to register `ILogger<T>` — caught by this migration's own integration test,
   fixed in `MindAttic.Log` (now `services.AddLogging(builder => builder.AddSerilog(...))`), see
   `docs/USER_STORIES.md` LOG-US-C1/C2. Both `MindAttic.Log.Tests` (9/9) and the full Automata suite
   (571/571) pass with the fix.
2. **Tutor ✅ (2026-10-08)** — originally assumed to be a SQL Server candidate (it has a SQL Server
   database), but that database turned out to be MindAttic.Authentication's identity schema only —
   general app logging there is a no-database problem, same tier as Automata/Launcher. Tutor also
   turned out to have its own hand-rolled `Log`/`LogStore` facade (not `ILogger<T>`) persisting to
   a single whole-file-rewrite JSON — the exact monolithic-file shape this repo's design rejected.
   `MindAtticLogBridge` forwards `LogStore.EntryAdded` into the rolled-SQLite tier with zero
   call-site changes. Full Tutor suite: 456/456 passing (plus 2 new tests for the bridge). This is
   the second confirmed case (after Launcher's `Console`/`AnsiConsole` calls) of an app with a
   logging call-site pattern that isn't `ILogger<T>` — worth checking explicitly for every
   remaining app rather than assuming the ecosystem-wide pattern from docs/MIGRATION.md's original
   survey holds everywhere.
3. **MindAttic.Launcher** — once current uncommitted work there lands, proves the no-DB/rolled-
   SQLite tier in the project this repo was built alongside. Needs care distinguishing real
   diagnostic logging from intentional `AnsiConsole` TUI rendering (see survey notes above) —
   not every `Console.Write*` call in a TUI app is a log line to extract.
4. **Prose (and KdpPublish/every other Prose.\* front door along with it)** — re-checked
   2026-10-08: KdpPublish has no logging of its own; everything routes through vendored
   `Prose.Core`. Migrating Prose.Core's pipeline migrates every front door that depends on it
   (Prose.Hub, KdpPublish, and any other `Prose.*` app) in one pass — there is no separate
   "KdpPublish-only" step. Last on purpose: Prose.Core already has a working pipeline under real
   use (14-day retention, live ring buffer, `LogIssue` triage) that nothing should regress
   casually, and re-vendoring a shared package touches every consumer at once.
5. **Ideas** — the one remaining confirmed candidate for an actual general-purpose SQL Server
   tier (`CmsDbContext`, not an auth-only database like Tutor's) — not yet re-checked the way
   Tutor and KdpPublish were, so its logging call-site pattern (ILogger<T> vs. something custom)
   needs confirming before assuming either way.

## Non-.NET apps

No non-.NET MindAttic app was found with a logging surface in this survey (MindAttic.Web's
`console.log` usage is browser-side JS, not a service that would adopt this package). If one
appears later, it needs a language-native writer that targets the same **wire schema**
(`docs/BIBLE.md` §4.1) — e.g. a Python module writing the same row shape into the same SQLite
table — rather than a port of the C# library.
