# ADR 0036 — Read caches keyed by the change-log stamp

**Status:** Accepted · 2026-10-09 · Phase 12
**Related:** ADR 0029 (change log), ADR 0020 (XP projection), DECISIONS F65

## Context

Phase 12 measured the app with two years of generated daily use (730 days,
~8 600 routine entries, ~12 600 activity events, ~10 500 XP lines). Two reads
were slow because they rebuild a "world" from all history on every request:

- `GamificationSync.RunAsync` (streak calendars, day verdicts, XP catch-up,
  celebrations) — ~300 ms; the sidebar level box calls it on every page, and
  each İstatistik request called it twice;
- `InsightsService.LoadAsync` (entries, sessions, sleep, closures, plans,
  streaks, XP by day) — 250–400 ms more per İstatistik request.

The SQL itself was fast; the cost is materialising and folding two years of
rows in C#. The answer only changes when data changes or the day turns.

## Decision

- **Version stamp = newest `sync_changes.Seq` + the logical day.** Every
  synced write adds a log row in its own transaction (outbox, ADR 0029), so
  an unchanged stamp means no synced row changed. `ChangeStamp
  .ChangeStampAsync(db)` is one primary-key seek.
- **One cached result per API host** (a singleton service, not a static, so
  test hosts with different databases never share it):
  `GamificationSnapshot` and `InsightsWorldCache`.
- **The key is the stamp read before the work.** The catch-up writes (XP
  lines, verdicts) and another request may write at the same time; keying
  with the earlier stamp makes the next read run once more and then settle.
  Never stale, at worst one extra run.
- Indexes added where `EXPLAIN QUERY PLAN` showed scans on hot paths:
  `routines_entries(LogicalDate)` (day/week/month views scanned the table),
  `activity_events(OccurredAtUtc, Id)` (matches the projection's ORDER BY,
  no temporary B-tree).

## Alternatives

- **Time-to-live cache.** Either stale right after a tick or useless; no
  good number exists.
- **Incremental streak/insight projections stored in tables.** Fastest
  reads, but a second source of truth to keep in sync with every rule
  change; the fold-from-facts design (Phase 7) stays.
- **Narrower loads per range.** Streaks and the karne need the whole run
  history anyway; the 7-day range was as slow as "all".

## Consequences

- Repeated reads cost a stamp query; measurements in the Phase 12 report.
- A write that bypasses change capture (`ExecuteUpdate`, raw SQL) on a table
  these worlds read would leave the cache stale. None exists today (raw SQL
  touches only `activity_day_summaries` and `today_queue_state`); new code
  must keep it that way or invalidate explicitly. Noted in both classes.
- The first read after a write still pays the full build; with two years
  of data that is a few hundred milliseconds, once.

## Interview angle

"How do you invalidate a cache?" → tie it to a version the writes already
maintain (row version, ETag, outbox sequence) rather than guessing a TTL.
