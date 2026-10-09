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
> every migration PR. ✅ migrated · 🟡 audited, not yet migrated · ⬜ not yet looked at ·
> **n/a** audited and correctly left alone (no real integration point, or a narrowly-scoped
> domain table that shouldn't be folded into `MindAttic_Log` — see each row's reasoning).
>
> Every row below has been individually re-checked against the real repo as of 2026-10-08 — do
> not trust stack/DB-backend columns from a prior pass without re-reading the actual code; several
> rows in the original 2026-10-08 survey turned out to be wrong (Tutor's "SQL Server" database is
> auth-only; Ideas.Library is archived; Mobile/Formicarium/MediaButler needed a closer look than
> the first grep-only pass gave them).

## Legend

- **Tier** — `SqlServer`, `Sqlite` (app-owned `.db`), or `Sqlite` (no-database, rolled file) — see
  `docs/BIBLE.md` §4.1/§4.2. The no-database tier is SQLite, not plain text.
- **n/a reasoning** — every n/a row was a deliberate decision, not a skip. Forcing a sink onto a
  stateless library or a pure-TUI tool invents a log where none of the app's real behavior calls
  for one — see LOG-LAW-1 (no per-app drift) and the "ecosystem pattern" note below.

## Status

| Project | Stack | DB backend | Tier | What was found / done | Status |
|---|---|---|---|---|---|
| **MindAttic.Ideas** | .NET Blazor | SQL Server (`CmsDbContext` — genuine general-purpose content DB: Sites, Pages, Media, Settings, Workflows, plus MindAttic.Authentication's identity tables mixed in) | SqlServer | `AddIdeasCore` calls `AddMindAtticLog` pointed at the same connection string `CmsDbContext` uses; table created once at startup (dev-only) via `LogSchema.CreateTableSqlServer`. **First live proof of the SQL Server tier** — caught and fixed a real bug (LOG-US-D1): `Level.StoreAsEnum` defaulted to storing level as text, which didn't fit the `TINYINT` column. Almost no prior `ILogger<T>` usage existed to extract (one call site) — this mainly establishes the pipeline going forward. Full suite: 586/586. | ✅ |
| **Tutor** | .NET Blazor | SQL Server, but auth-only (`TutorAuthDbContext` is MindAttic.Authentication's identity schema; "course content and per-user progress stay JSON" per its own doc comment) — general logging is a no-database problem here | Sqlite (rolled file) | Had its own hand-rolled `Log`/`LogStore` facade (`Log.Info`/`Warn`/`Error`/`Critical`), persisted by `LogStorageService` as a single `app-logs.json` **rewritten whole on every save** — the exact monolithic-file anti-pattern this repo's design rejected. `MindAtticLogBridge` forwards `LogStore.EntryAdded` into the rolled-SQLite tier, zero call-site changes, existing JSON persistence and live in-app viewer left untouched. Full suite: 456/456 (+2 new). | ✅ |
| **Automata** | .NET + Node tools | SQLite (`AutomataDb`, EF Core) | Sqlite (app-owned) | `AddAutomataCore` calls `AddMindAtticLog` pointed at `AutomataDatabase.ResolvePath()` — same file EF owns. `ILogger<T>` call sites unchanged. This run caught the original `services.AddSerilog(...)` vs `services.AddLogging(...)` bug (LOG-US-C1). Full suite: 571/571 (+1 new). | ✅ |
| **MindAttic.Mobile** | .NET (ASP.NET Core minimal API) | none | Sqlite (rolled file) | WebSocket + xterm.js terminal bridge, single-file app, no database, no test project. Wired `AddMindAtticLog` (rolled-file tier); converted the two startup `Console.WriteLine` calls to `app.Logger`, threaded `ILogger` into `TerminalSession` for its one real error path (Claude API call failures). No test project exists, so verified **live**: ran the built exe, confirmed real log lines landed in the rolled `.db` file. | ✅ |
| **Formicarium** | .NET Blazor Server (Dashboard) + separate firmware | SQL Server (`FormicariumDbContext` — genuine general-purpose DB: Samples, RiserSamples, BuildProgress; no auth tables mixed in) | SqlServer | `ColonyMonitor`/`TelemetryStore` already used `ILogger<T>`. Wired `AddMindAtticLog` (SQL Server tier) in `Program.cs`, table created at startup next to the existing EF migration. No Dashboard test project exists — verified **live** against the real dev LocalDB: app started cleanly, **20 real rows landed in `dbo.MindAttic_Log`**. Added a repo-local `nuget.config` (none existed). | ✅ |
| **MediaButler** | .NET CLI (Spectre.Console) + WPF (BlazorWebView) | none | Sqlite (rolled file) | No prior diagnostic logging in either front door — the WPF app had **zero crash visibility** before this (no `DispatcherUnhandledException`/`AppDomain.UnhandledException` handlers at all). Added `CrashLog` (rolled-file tier) wired into both front doors' now-new unhandled-exception handling. A pre-existing NDJSON `AuditLog` (file-mutation records: op/kind/from/to) was correctly left untouched — a narrowly-scoped business record, not a candidate for folding into `MindAttic_Log` (same reasoning as `AuthAuditLog`). Full suite: 298/298 (+1 new). | ✅ |
| **Prose** | .NET | SQL Server (`ProseDbContext`, database `Prose` — genuine general-purpose DB: Entities, Nodes, LogIssue, etc.) | SqlServer (additive) | Serilog daily text files (`Prose.Hub/Program.cs`) parsed back by `LoggingService`; separate derived `LogIssue` triage table — the reference model this schema was built from (BIBLE §4.1). Migrated **additively, not as a replacement**, given the stakes: `Program.cs` registers `MindAttic.Log` as a THIRD `builder.Logging.AddSerilog(...)` provider (same pattern the file already uses twice), writing `MindAttic_Log` into the SAME `Prose` database — nothing about the existing file pipeline, `LoggingService.Search`, or `LogIssue` triage changed, rerouted, or removed. Table created once at startup, reusing the exact connection string Prose's own real EF migration step just resolved (no drift risk). Verified via a new `[Explicit]` test (own throwaway LocalDB database, never the real `Prose` DB) — did **not** also replay `ProseDbContext`'s full migration history to prove table coexistence the way Ideas/Automata's tests do, because that replay currently fails from scratch on a pre-existing, unrelated `NodeCode` index/column-type error (invisible in production only because the live DB already has every migration applied) — flagged, not fixed; out of scope here. The live Hub was redeployed and restarted on 2026-10-08 via its own official mechanism (`deploy-hub.bat` → `deploy-apps.ps1 -Apps Hub -Start Hub`) after explicit user confirmation — not bounced ad hoc. Confirmed via `netstat` (new PID) and a direct query against the real `Prose` database: 5 genuine rows landed immediately, including real startup diagnostics ("GlobalSearch index warmed in 7101 ms", "HomeStats refreshed in 137 ms", live auth/SMTP config lines) — the sink is live in production, not just committed. Note: only `Prose.Hub` was touched, not vendored `Prose.Core` — see KdpPublish's row below, still correctly unaffected. | ✅ |
| **MindAttic.Launcher** | .NET (net10.0-windows), no DI container (Spectre.Console.Cli constructs commands directly) | none | Sqlite (rolled file) | Had zero crash visibility of any kind. Wired `MindAttic.Log` at the one place every run passes through regardless — `Program.cs` itself (`WriteToMindAtticLog` on a raw `LoggerConfiguration`, since there's no `IServiceCollection` to call `AddMindAtticLog` on) — with a top-level try/catch logging any unhandled exception as Fatal before rethrowing. Deliberately did **not** touch `HostAgentCommand.cs`/`AgentProviderRegistry.cs`/`ClaudeStatusService.cs` (unrelated in-progress changes in the working tree) or any of the 17 files' `Console.Write*`/`AnsiConsole.MarkupLine` calls — those are the TUI's actual menu output, not diagnostics to extract (same reasoning as MindAttic.Deploy). Verified live: ran `version`, confirmed the rolled file + table are created cleanly with 0 rows (correct — that command logs nothing). Full suite: 173/173, unaffected. | ✅ |
| KdpPublish | .NET (WPF) | none | — | No logging surface of its own. Its only own-code "logging" is a `#if DEBUG`-only `File.AppendAllText` crash dump, deliberately dependency-free (same reasoning Automata.App uses for its own crash log) — not a sink candidate. Still correctly n/a after Prose's migration: that touched `Prose.Hub`'s own `Program.cs` only, not vendored `Prose.Core` (deliberately, to avoid a duplicate sink registration — `Prose.Hub` itself also calls `AddProseServices()`), and KdpPublish has no `ILogger<T>` call sites of its own that a `Prose.Core`-level registration would have reached anyway. | n/a |
| MindAttic.Authentication | .NET | Host-provided (SQL Server typical) | — | `ILogger<T>` + custom `AuthAuditWriter` → `AuthAuditLog` table. Narrowly-scoped security-audit schema (event type, outcome, hashed IP, user agent) — independently confirmed by two separate migrations (Tutor, MediaButler) as the right pattern to *not* fold into general `MindAttic_Log` rows. | n/a — separate audit schema by design |
| MindAttic.Deploy | .NET CLI (Spectre.Console TUI) | none | — | No DI host, no `ILogger<T>`. `AnsiConsole.MarkupLine(...)` output IS the interactive deploy-progress UI shown to the operator, not a diagnostic log — same shape as Launcher's deferred case. | n/a |
| MindAttic.Export | .NET (library + CLI) | none | — | Pure, stateless, one-shot manuscript-rendering library (Markdown → PDF/DOCX/EPUB). Zero `ILogger<T>` anywhere; the CLI's only output is a single `Console.Error.WriteLine` on bad arguments. No logging concept exists to route. | n/a |
| MindAttic.Helpers | .NET (library) | none | — | Two tiny static-utility files (`AbstractArtGenerator.cs`, `PiHelper.cs`). No DI, no database, no logging surface. | n/a |
| MindAttic.Bob | PowerShell (`bob.ps1`/`bob.bat`) | none | — | Zero `.csproj` files — not a .NET application at all. Zips `%APPDATA%\MindAttic` and shells out to `sqlcmd` for backups. MindAttic.Log has no PowerShell-side equivalent. | n/a — non-.NET |
| MindAttic.Cryptography | — | none | — | Confirmed empty placeholder (its own README says so explicitly) — planned but not started. Nothing to integrate, same state MindAttic.Log itself was in before this session. | n/a — placeholder repo |
| MindAttic.Ideas.Library | .NET (Razor Class Libraries) | none | — | Confirmed **retired/archived** — merged into `MindAttic.Ideas/library` on 2026-06-12, GitHub repo archived. Zero `ILogger`/DI/`Program.cs` — compiled UI citizens consumed by MindAttic.Ideas at runtime (already migrated), not a standalone service. | n/a — archived |
| MindAttic.Media | .NET (library, + Azure variant) | none | — | Pure class library with no DI composition root of its own (`AddMedia<T>()` is called *by* a consuming app's container, e.g. Ideas'). Any future logging flows through automatically once the consuming app has its own pipeline wired — no separate integration point. | n/a |
| MindAttic.Psst | .NET CLI | none | — | Short-lived passthrough wrapper (`psst -- <command>`); entire purpose is piping output to the terminal. No DI container, no `ILogger` anywhere, explicitly "no daemon, nothing listening." Opening a SQLite connection per few-second invocation for 1-2 status lines would cost more than it's worth against the tool's own minimal-footprint design. | n/a |
| MindAttic.Web | Static HTML sites + Node | none | — | No .NET project anywhere in the repo (mindattic.com, mindatticcares.com, ryandebraal.com are static sites; the rest is a JS test package). No integration point for a .NET package. | n/a — non-.NET |
| **MindAttic.Vault.Dashboard** | .NET (ASP.NET Core Blazor Server) | none | Sqlite (rolled file) | The LLM health-monitor web app (`HealthMonitorStore`/`SelfHealer`/`AlertDispatcher`/`MonitorBackgroundService`) already called `ILogger<T>` throughout but had no durable sink. Wired `AddMindAtticLog` (rolled-file tier, `%LocalAppData%\MindAttic\VaultDashboard\logs`) into `Program.cs`. No test project exists for the Dashboard (only the `MindAttic.Vault` library has one) — verified **live**: ran the built app, confirmed 61 real rows landed, including `MonitorBackgroundService`'s own startup line. Library's own suite (293/293) unaffected. Note: `MindAttic.Vault` the *library* has no DI host of its own and needs no separate action — only the Dashboard app does. | ✅ |
| MindAttic.Legion | .NET (library + `.Cli`) | none | — | The library (`LegionClient`, `LlmVotingService`, etc.) has no DI composition root of its own — `ILogger<T>` it requests resolves from whichever consuming app's container (Automata/Tutor/Ideas/Vault.Dashboard, all already migrated), so it needs no separate integration. `MindAttic.Legion.Cli` is a one-shot command dispatcher (`ask`/`vote`/`poll`/etc.) whose `Console.WriteLine` output IS its interactive result (built to pipe back into another CLI) — same shape as Deploy/Psst, correctly not a sink candidate. | n/a |
| ChiMesh | PowerShell (LoRa/Meshtastic node provisioning) | none | — | Pure PowerShell hardware-provisioning CLI, no .NET at all. | n/a — non-.NET |
| GridGame2026 | Unity/C# | n/a | — | Unity's own logging; not a service app — out of scope for this pipeline. | skip |

## Ecosystem pattern (confirmed across the full pass)

`ILogger<T>` is the near-universal call-site API for apps that have one at all — which is exactly
why `services.AddMindAtticLog(...)` targets the DI/Serilog seam instead of asking every app to
change its logging calls. Two confirmed exceptions needed a different approach: Tutor's own
`Log`/`LogStore` facade (bridged directly, not through DI) and MediaButler's WPF app, which had no
logging concept at all before this pass (not even unhandled-exception handling) and got one built
fresh. A large fraction of the ecosystem — roughly half of what was checked — turned out to have
**no real integration point**: pure libraries with no DI host of their own, stateless one-shot
tools, PowerShell scripts with no .NET surface, static sites, archived repos, and apps whose only
"logging" is actually their interactive TUI output. Forcing a sink onto any of those would have
meant inventing a log where the app's real behavior never called for one — each is recorded above
with its specific reasoning rather than silently skipped.

## What's left

Nothing. Every MindAttic repo has been individually audited, and every one with a real
integration point has been migrated. Prose's additive sink is live in its production Hub process
(redeployed and restarted 2026-10-08 via `deploy-hub.bat`, with explicit user confirmation first —
verified by direct query against the real database, not just a build).

One boundary is worth stating plainly, since it was confirmed by explicit user decision rather
than assumed: **MindAttic.Launcher's 17-file `Console.Write*`/`AnsiConsole.MarkupLine` surface
stays as is.** That output is the TUI's own menu rendering, not diagnostics — logging
infrastructure gets the crash/error path (which Launcher now has via its entry-point wiring), not
an app's interactive UI. See `docs/BIBLE.md` §3 for this and the other three boundaries of what
"one pipeline" means, each confirmed the same way.

## Non-.NET apps

Confirmed in this pass: MindAttic.Bob and ChiMesh are pure PowerShell with no .NET surface;
MindAttic.Web is static HTML/JS with no .NET project at all. None had a logging surface this
package could attach to. If a non-.NET app with real logging needs ever appears, it needs a
language-native writer that targets the same **wire schema** (`docs/BIBLE.md` §4.1) — e.g. a
Python module writing the same row shape into the same SQLite table — rather than a port of the
C# library.
