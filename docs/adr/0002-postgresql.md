# ADR 0002 — PostgreSQL as system of record

**Status:** Accepted

## Context
The system needs relational integrity (terminals, alerts, commands, users, audit), moderate time-series volume
(telemetry and logs), and simple operations. Many banks already run PostgreSQL or SQL Server.

## Decision
PostgreSQL via EF Core (Npgsql) in production, with versioned migrations in `AtmMonitor.Infrastructure/Persistence/Migrations`.
SQLite is supported for local development and tests only: the schema is created with `EnsureCreated`, and value
converters make `DateTimeOffset` and `decimal` sortable and aggregatable.

## Consequences
- ✅ One engine for both relational and time-series data at the target scale (thousands of terminals).
- ✅ Clear migration path to TimescaleDB (same wire protocol) or time partitioning if telemetry grows.
- ⚠️ SQL Server shops need a second migrations assembly. The model has no provider-specific features, so this is mechanical.
- ⚠️ SQLite is not a supported production database: no migrations, no concurrent writers.
