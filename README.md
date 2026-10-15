# MindAttic.Log

One strict logging schema for every MindAttic app, written to one standalone `MindAttic.Log.db` SQLite database per app, stored under MindAttic.Vault's shared roaming root rather than any one app's own data folder.

[![.NET](https://img.shields.io/badge/.NET-9.0%20and%2010.0-512BD4)](MindAttic.Log/MindAttic.Log.csproj) [![C#](https://img.shields.io/badge/language-C%23-239120)](MindAttic.Log) [![Version](https://img.shields.io/badge/version-3.0.0-blue)](MindAttic.Log/MindAttic.Log.csproj) [![Tests](https://img.shields.io/badge/tests-NUnit-brightgreen)](MindAttic.Log.Tests) [![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## Why

Across the MindAttic ecosystem (see `docs/MIGRATION.md`), most apps already call `ILogger<T>` at their call sites but have no durable sink underneath — output goes nowhere, or to ad hoc `Console.Write*`/hand-rolled file writers that nobody can filter or query. MindAttic.Log replaces all of that with one package: apps keep calling `ILogger<T>`, and this library becomes the sink.

A flat text file is also a dead end for anything but `tail`/`grep` — there's no index, and "one giant monolithic log" degrades as it grows. Prior art (Blacklite, logdive — see `docs/BIBLE.md` §4) found that an embedded SQLite file beats parsing flat text for queryability while staying what a "log file" is supposed to be: one portable file, no server.

Every app gets its own standalone `MindAttic.Log.db`, full stop — no sharing an app's SQL Server database or its own SQLite file, and no per-app location choice. The file lives at `%APPDATA%\MindAttic\Logging\<appId>\MindAttic.Log.db` (resolved via `MindAttic.Vault`'s `VaultPaths.LoggingDbPath`), deliberately under Vault's shared **roaming** root rather than the app's own `%LocalAppData%` — so clearing an app's local cache/data folder can never take its logs with it. Retention is row-based (delete-then-`VACUUM` on a schedule), not file-rolling, since the file is never replaced.

## What's in this repo

- **`MindAttic.Log`** — the shared library. `LogEntry`/`LogSeverity` (the wire schema), `LogSchema` (the DDL), a custom batched `ILogEventSink` for SQLite (WAL mode, so a Reader can query live, plus a row-based retention sweep), `services.AddMindAtticLog(...)` to wire it into DI, and `MindAtticLogMigrator` — a one-shot, explicitly-invoked tool for copying an app's historical rows out of the retired SQL Server/app-owned-SQLite tiers (see `docs/MIGRATION.md`).
- **`MindAttic.Log.Reader`** — a WPF + WebView2 desktop viewer (same shape as Automata.App/KdpPublish, not Blazor) that discovers every app's standalone database under Vault's `Logging` bucket and lets you switch between them. Filters by date range, level, application, category, and message text.
- **`docs/BIBLE.md`** — architecture canon: why this schema, why SQLite, the laws.
- **`docs/MIGRATION.md`** — tracks which MindAttic apps have been audited and migrated.

## Using it

```csharp
services.AddMindAtticLog(o =>
{
    o.Application = "MindAttic.Launcher";
    o.DbPath = VaultPaths.LoggingDbPath("MindAttic.Launcher"); // resolved via MindAttic.Vault
});
```

Application code is unchanged: inject `ILogger<T>` and call it as usual.

## Building

```
dotnet build MindAttic.Log.slnx
dotnet test MindAttic.Log.slnx
```

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic).
