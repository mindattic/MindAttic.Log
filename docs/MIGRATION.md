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
| **Prose** | .NET | SQL Server | SqlServer | Serilog daily text files (`Prose.Hub/Program.cs`) parsed back by `LoggingService`; separate derived `LogIssue` triage table. Reference model for the schema (BIBLE §4.1). **Deliberately deferred**: live production pipeline (14-day retention, live ring buffer, `LogIssue` triage) already relied on; re-vendoring `Prose.Core` touches every `Prose.*` front door (Hub, KdpPublish) at once. Holding for explicit go-ahead rather than migrating unilaterally. | 🟡 |
| **MindAttic.Launcher** | .NET (net10.0-windows) | none | Sqlite (rolled file) | Raw `Console.Write*`/`AnsiConsole.MarkupLine` across 17 files — no structured logging at all. **Blocked**: has uncommitted changes in exactly the files (`HostAgentCommand.cs`, `AgentProviderRegistry.cs`, `ClaudeStatusService.cs`) a migration would touch; held per explicit instruction not to step on in-progress work. Needs care distinguishing real diagnostic logging from intentional `AnsiConsole` TUI rendering when it is picked up — not every `Console.Write*` call in a TUI app is a log line to extract. | ⬜ |
| KdpPublish | .NET (WPF) | none | — | No logging surface of its own. Its only own-code "logging" is a `#if DEBUG`-only `File.AppendAllText` crash dump, deliberately dependency-free (same reasoning Automata.App uses for its own crash log) — not a sink candidate. All real logging flows through vendored `Prose.Core`; migrating it is the same work as migrating Prose, not a separate step. | n/a — folds into Prose |
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
| MindAttic.Vault | .NET | none (settings store) | Sqlite (rolled file) | `ILogger<T>` in `AlertDispatcher`, `MonitorBackgroundService`; no durable sink. Not yet picked up — lower priority than apps with real logging volume, since Vault's own operations are infrequent. | ⬜ |
| MindAttic.Legion | .NET | none | Sqlite (rolled file) | `Microsoft.Extensions.Logging` referenced in DI extensions; no durable sink. Not yet picked up. | ⬜ |
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

1. **MindAttic.Launcher** — blocked on your uncommitted changes landing first.
2. **Prose** (and every `Prose.*` front door that depends on vendored `Prose.Core`, including
   KdpPublish) — held for explicit go-ahead given it's a live production pipeline, not migrated
   unilaterally.
3. **MindAttic.Vault** / **MindAttic.Legion** — have `ILogger<T>` call sites but no durable sink
   yet; lower priority (infrequent operations) than the apps already done, not yet picked up.

## Non-.NET apps

Confirmed in this pass: MindAttic.Bob and ChiMesh are pure PowerShell with no .NET surface;
MindAttic.Web is static HTML/JS with no .NET project at all. None had a logging surface this
package could attach to. If a non-.NET app with real logging needs ever appears, it needs a
language-native writer that targets the same **wire schema** (`docs/BIBLE.md` §4.1) — e.g. a
Python module writing the same row shape into the same SQLite table — rather than a port of the
C# library.
