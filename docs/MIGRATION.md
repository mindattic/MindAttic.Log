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
| Tutor | .NET Blazor | SQL Server (`TutorAuthDbContext`) | SqlServer | `ILogger` in `Tutor.Mcp`; no durable sink found. | ⬜ |
| Automata | .NET + Node tools | SQLite (`AutomataDb`, EF Core) | Sqlite (app-owned) | Migrated: `AddAutomataCore` calls `AddMindAtticLog` pointed at `AutomataDatabase.ResolvePath()` — same file EF owns. `ILogger<T>` call sites (`WorkflowEngine`/`FlowAuthoringService`/`ReplayEngine`/etc.) are unchanged; only the sink underneath is new. | ✅ |
| MindAttic.Launcher | .NET (net10.0-windows) | none | Sqlite (rolled file) | Raw `Console.Write*`/`AnsiConsole.MarkupLine` across 17 files (Commands/, Menus/, Services/, Ui/) — **no structured logging at all**. First integration target: no existing pipeline to reconcile with. | ⬜ |
| KdpPublish | .NET (WPF) | none | Sqlite (rolled file) | Hand-rolled file logger in `App.xaml.cs` (`File.AppendAllText`-style). Best second no-DB candidate — already proves the WPF+WebView2 shell shape this repo's Reader reuses. | ⬜ |
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
2. **MindAttic.Launcher** — once current uncommitted work there lands, proves the no-DB/rolled-
   SQLite tier in the project this repo was built alongside. Needs care distinguishing real
   diagnostic logging from intentional `AnsiConsole` TUI rendering (see survey notes above) —
   not every `Console.Write*` call in a TUI app is a log line to extract.
3. **KdpPublish** — second no-DB app; replaces a hand-rolled file logger, and already shares the
   Reader's WPF+WebView2 shell shape.
4. **Prose** — the reference model itself; migrating it last (not first) because it already has a
   working pipeline under real use (14-day retention, live ring buffer, `LogIssue` triage) that
   nothing should regress casually.
5. A SQL Server app (Ideas or Tutor) — proves the SQL Server tier against a real production schema.

## Non-.NET apps

No non-.NET MindAttic app was found with a logging surface in this survey (MindAttic.Web's
`console.log` usage is browser-side JS, not a service that would adopt this package). If one
appears later, it needs a language-native writer that targets the same **wire schema**
(`docs/BIBLE.md` §4.1) — e.g. a Python module writing the same row shape into the same SQLite
table — rather than a port of the C# library.
