---
codex: 1
project: MindAttic.Log
code: LOG
layer: amendments
status: living
updated: 2026-10-08
---

# MindAttic.Log — Pending decisions
> Decisions not yet folded into docs/BIBLE.md. Normally empty: fold each into the bible and delete it.

- SQL Server connection-string resolution for `MindAttic.Log.Reader` (LOG-US-E3) still takes a
  raw string typed into the session; whether it should resolve named credentials through
  MindAttic.Vault's broker store, or something simpler (a recent-connections list with Windows
  Credential Manager), isn't decided yet.
- Whether a derived triage layer (Prose's `LogIssue` pattern, generalized) belongs in this repo as
  `MindAttic.Log.Issues` or stays a per-app concern is explicitly deferred — see BIBLE §3.
