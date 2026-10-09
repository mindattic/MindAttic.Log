# MindAttic.Log

One strict logging schema for every MindAttic app, written to whatever storage the app already has: a `MindAttic_Log` table on SQL Server, a `MindAttic_Log` table in the app's own SQLite database, or a dedicated rolling SQLite file when the app has no database at all.

[![.NET](https://img.shields.io/badge/.NET-9.0%20and%2010.0-512BD4)](MindAttic.Log/MindAttic.Log.csproj) [![C#](https://img.shields.io/badge/language-C%23-239120)](MindAttic.Log) [![Version](https://img.shields.io/badge/version-1.0.0-blue)](MindAttic.Log/MindAttic.Log.csproj) [![Tests](https://img.shields.io/badge/tests-NUnit%204-brightgreen)](MindAttic.Log.Tests) [![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## Why

Across the MindAttic ecosystem (see `docs/MIGRATION.md`), most apps already call `ILogger<T>` at their call sites but have no durable sink underneath — output goes nowhere, or to ad hoc `Console.Write*`/hand-rolled file writers that nobody can filter or query. The one app with a real logging pipeline, Prose, writes daily Serilog text files that get parsed back with regex to answer even simple questions ("what errors happened last Tuesday"). MindAttic.Log replaces all of that with one package: apps keep calling `ILogger<T>`, and this library becomes the sink.

A flat text file is also a dead end for anything but `tail`/`grep` — there's no index, and "one giant monolithic log" degrades as it grows. Prior art (Blacklite, logdive — see `docs/BIBLE.md` §4) found that an embedded SQLite file beats parsing flat text for queryability while staying what a "log file" is supposed to be: one portable file, no server. So the "file" tier in MindAttic.Log *is* SQLite — rolled monthly, same idea as Prose's daily rotation — not plain text.

## What's in this repo

- **`MindAttic.Log`** — the shared library. `LogEntry`/`LogSeverity` (the wire schema), `LogSchema` (the DDL, identical columns on both backends), a custom batched `ILogEventSink` for SQLite (WAL mode, so a Reader can query live), `Serilog.Sinks.MSSqlServer` wiring for the SQL Server tier, and `services.AddMindAtticLog(...)` to wire it all into DI.
- **`MindAttic.Log.Reader`** — a WPF + WebView2 desktop viewer (same shape as Automata.App/KdpPublish, not Blazor) for browsing logs across all three tiers: open a folder of rolled SQLite files, open a single app's `.db`, or connect to a SQL Server table. Filters by date range, level, application, category, and message text.
- **`docs/BIBLE.md`** — architecture canon: why this schema, why SQLite for the file tier, the laws.
- **`docs/MIGRATION.md`** — tracks which MindAttic apps have been audited and migrated.

## Using it

```csharp
services.AddMindAtticLog(o =>
{
    o.Application = "MindAttic.Launcher";
    o.Destination = LogDestination.Sqlite;
    o.FileDirectory = VaultPaths.AppData("MindAttic.Launcher", "logs"); // no database → rolled file tier
});
```

```csharp
services.AddMindAtticLog(o =>
{
    o.Application = "MindAttic.Ideas";
    o.Destination = LogDestination.SqlServer;
    o.SqlServerConnectionString = connectionString; // resolved via MindAttic.Vault upstream
});
```

Either way, application code is unchanged: inject `ILogger<T>` and call it as usual.

## Building

```
dotnet build MindAttic.Log.slnx
dotnet test MindAttic.Log.slnx
```

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic).
