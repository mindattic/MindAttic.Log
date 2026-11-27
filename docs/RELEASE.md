---
layer: release
status: living
---
# MindAttic.Log — Release charter

> The one finish line this repo is working toward. Every open ticket in this repo sits under one of the epics
> below, and every epic is part of the milestone. Work that does not fit is out of scope until this milestone is
> reached (owner, 2026-11-27, MAT-T319).

## 1. What this is
The one logging pipeline for every MindAttic app: a strict schema written through `ILogger<T>` to a per-app
`MindAttic.Log.db` (or the shared SQL Server `MindAttic.Log` database), plus the WPF Log Reader.

## 2. The milestone: MindAttic.Log 4.0 {#milestone}
- **Milestone:** MS-50, "MindAttic.Log 4.0: two stores, guarded sink"
- **Published means:** `MindAttic.Log.4.0.0.nupkg` on `C:\LocalNuGet`; the Reader deploys to
  `R:\MindAttic\Apps\MindAttic.Log`.
- **The natural stopping point:** the SQL Server store, the store registry and the SQLite-to-SQL-Server migrator
  landed after 3.0.0 was packed, so no consumer has them yet. Releasing them as 4.0.0, with the never-throw sink
  contract under test and the canon docs describing both stores, leaves a complete logging library.

## 3. Done when (acceptance checks)
1. build — `MindAttic.Log.slnx` builds clean on main.
2. exists — `C:\LocalNuGet\MindAttic.Log.4.0.0.nupkg`.
3. signoff — the owner opens the Log Reader and sees both a SQLite and a SQL Server source.

## 4. Epics, in priority order
| Priority | Epic (ticket) | What it delivers | Features |
|---|---|---|---|
| 900 | M.LOGS-T38 Sink contract hardening | Tests guard never-throw and fail-fast registration | M.LOGS-T39 SQLite sink resilience (990: M.LOGS-T5), M.LOGS-T40 Registration validation (980: M.LOGS-T6) |
| 800 | M.LOGS-T41 4.0 release and docs | 4.0.0 packed; BIBLE and MIGRATION current | M.LOGS-T42 Package 4.0.0 (890: M.LOGS-T43), M.LOGS-T44 Docs current (880: M.LOGS-T8, M.LOGS-T45) |

## 5. Out of scope (not before this milestone)
- A log aggregation server or ingestion API.
- `MindAttic.Log.Issues` (LogIssue-style triage over raw logs).
- Moving the ~20 consumers from 3.0.0 to 4.0.0 — their own repos' work.
- More migrator edge-case tests (one-shot tool), column-mapping audit tests, bughunts, sweeps.

## 6. Current verified state
- 3.0.0 on `C:\LocalNuGet`, consumed by Prose, Orchestrator, Tickets, Vault, Launcher, IdiotProof and others.
- SQL Server store and registry: bf5054b, 2a016bd; Reader per-store readers: 5a9a126; migrator: 25869dc.
- README documents both stores (25869dc).

## 7. Decision record
- 2026-11-27 — charter drafted from README, BIBLE, git log and tickets; Legion vote: **Approve**, 5 of 9 voters
  answered, plurality quorum reached, consensus 1.0, no dissent.
- Triage: 9 open → done 1 (T12), duplicate 0, wont-do 5 (T4, T7, T10, T11, T34), kept 3 (T5, T6, T8),
  new tasks 2 (T43, T45).
