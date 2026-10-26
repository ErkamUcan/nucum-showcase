# ADR 0013 — Store DateTimeOffset columns as UTC ticks (Int64)

**Status:** Accepted · 2026-10-03 · Phase 4a
**Supersedes:** —

## Context

Nücum uses modular monolith EF Core 10 contexts over a shared SQLite file
(ADR 0003). Every module declares its timestamp columns as `DateTimeOffset`
or `DateTimeOffset?` (ARCHITECTURE §6.1 "Ortak alanlar").

SQLite's EF Core provider rejects `ORDER BY` on `DateTimeOffset`:

```
System.NotSupportedException: SQLite does not support expressions of type
'DateTimeOffset' in ORDER BY clauses. Convert the values to a supported
type, or use LINQ to Objects to order the results on the client side.
```

The same restriction kills range filters and index seeks on timestamp
columns. We hit this immediately in Phase 4a when the Collections module
tried to sort hata-defteri entries by `last_review_at` (least-reviewed
first) and by `created_at` (newest first).

## Decision

Store every `DateTimeOffset` / `DateTimeOffset?` column as a `long` of
**UTC ticks** at the ORM layer, via a shared EF Core convention applied
by every DbContext.

- A single `ValueConverter<DateTimeOffset, long>` and its nullable
  counterpart live in `Nucum.SharedKernel.Time.DateTimeOffsetConvention`.
- `ModelConfigurationBuilder.ApplyDateTimeOffsetAsTicks()` registers both
  converters model-wide via `Properties<T>().HaveConversion<...>()`.
- Each module's DbContext calls that extension from
  `ConfigureConventions(...)`. One line per context, no entity changes.
- On the way in we `ToUniversalTime()` first; on the way out we build the
  result at `TimeSpan.Zero`.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| **Client-side sorting** (materialise rows, order in memory) | Correct but discards indices, pushes work to .NET, breaks pagination and prevents efficient "top N" via the DB. Our hata-defteri rows are small today but the same column type lives in Activity (append-only, grows forever) and Routines entries. |
| **ISO-8601 TEXT column** | SQLite can `ORDER BY` lexical ISO-8601 safely, but we lose a fast scalar comparison, we have to think about offsets (lexical order of `+03:00` strings is wrong), and migrations become string-munging. Also ~24 bytes per cell vs. 8 bytes for an Int64. |
| **Separate `offset_minutes` column** | Preserves the original offset. Nothing in Nücum needs the original offset (all producers use `TimeProvider.GetUtcNow()`); the extra column would be dead weight. |
| **Entity-side `long _ticks` + `DateTimeOffset` computed** | Needs a field per entity, breaks `init`-only records, and leaks storage detail into the domain. The convention approach keeps the entities clean. |

## Consequences

- **ORDER BY, range filters, and index seeks on timestamps now run in SQLite.** Collections'
  "least reviewed" and "newest first" queries no longer materialise + sort
  client-side.
- **Deliberate offset loss.** Writing a local `DateTimeOffset(…, +03:00)` reads
  back as `DateTimeOffset(…, +00:00)`. Nücum's producers already use
  `TimeProvider.GetUtcNow()` (SharedKernel.Time), so this is a no-op in
  practice. The convention makes the normalisation explicit.
- **Backwards compatible for a fresh DB.** `ModuleSchema.EnsureTablesCreatedAsync`
  regenerates CREATE statements from the live model, so the first launch
  on a new machine writes `INTEGER` columns from day one. There is no
  Phase 1–3 production data to migrate yet.
- **If/when real EF Migrations arrive (Phase 4+ per ARCHITECTURE §6.2
  footnote),** the first migration must change existing timestamp columns
  from TEXT to INTEGER via a ticks-encoding backfill. Noted here so future
  work does not re-decide.
- **Indices on timestamp columns now behave normally.** `CreatedAt`,
  `LastReviewAt`, `OccurredAtUtc`, `UpdatedAt` can be indexed and used
  for efficient paging / windowing in later phases (Insights, Backup).

## Related

- ARCHITECTURE §6.1 (common fields on all entities)
- SPEC §2 "Her hareket olay olarak kaydedilir" — the append-only log
  stresses timestamp ordering more than any other module.
- ADR 0003 (SQLite + EF Core per-module DbContext)
- ADR 0005 (events + outbox — outbox ordering also benefits).
