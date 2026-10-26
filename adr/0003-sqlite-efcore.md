# ADR 0003 — SQLite + EF Core, one DbContext per module

Date: 2026-10-02 · Status: Accepted

## Context
Nücum stores plans, routines, events, sleep logs, and so on, on the
user's own machine. Writes come from one user, mostly serialised.
Reads can be large (stats, insights). The product may be lifted to a
server-side variant later.

## Decision
Use **SQLite in WAL mode**, one file, with **EF Core 10**. Each module
owns its tables (prefixed by module id, e.g. `routines_routines`) and its
own `DbContext`. No shared DbContext across modules. Cross-module writes
go through integration events; cross-module reads use Contracts.

The `TreatWarningsAsErrors` policy applies to EF code paths. All queries
set `AsNoTracking()` on the read side; writes stay inside command handlers.

## Alternatives
- **LiteDB / Realm:** fewer migrations, worse SQL ergonomics, worse
  tooling. SQLite is the industry workhorse.
- **PostgreSQL embedded:** heavy for a single-user app; port and lifecycle
  management for a desktop install is more than it's worth.
- **One monolithic DbContext:** easier queries, but blurs module ownership
  and makes it harder to split later.

## Consequences
- (+) Zero-admin storage; single `.db` file to back up.
- (+) WAL + a short busy timeout handles the one-writer / many-readers
  pattern in-process.
- (+) Clear migration path to Postgres: swap provider, keep DbContext per
  module, re-tune indexes.
- (−) Per-module DbContext means light duplication (connection string,
  base entity config); handled by a small base class in SharedKernel.
- (−) SQLite does not enforce check constraints the way Postgres does;
  invariants live in the domain layer.
