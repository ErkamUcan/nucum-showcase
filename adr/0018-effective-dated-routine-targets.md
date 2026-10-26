# ADR 0018 — Effective-dated routine targets

**Status:** Accepted · 2026-10-07 · Phase 6.5 (Durak B)

## Context

From Faz 6.5/B on, the user can edit a routine's target in the app ("15 pages"
→ "20 pages"). Streaks and statistics (Phase 7) look back over weeks and
months. If the target were one overwritten value, raising it would turn old
15-page days into "not done" days after the fact, and filling in a past day
would judge it against today's target.

## Decision

- `routines_routines.TargetJson` keeps the **current** target.
- A new table `routines_target_history (RoutineId, EffectiveFrom, TargetJson)`
  keeps periods. A period starts on a logical day (ADR 0006) and lasts until
  the next one. `DateOnly.MinValue` means "since the beginning". An empty
  history means the current target has always applied.
- A change on day T adds a period starting on T (the first change also writes
  the old target as the "since the beginning" period). Two changes on one day
  stay one period; editing back to the previous target removes the period.
- Every write that judges completion (count, duration, page range, clearing
  an automatic mark) uses the target **of the entry's day**
  (`TargetHistory.TargetOn`). The stored `Done` of old entries is never
  recomputed.
- `RoutineDto.TargetHistory` exposes the periods, so the UI (week circles,
  partial rings) and Phase 7 read the same answer.

## Alternatives

- **Copy the target into every entry.** Works for days with an entry, but a
  missed day has no entry, and "what was the goal that day" is exactly what a
  missed day needs.
- **Version the whole routine row (temporal table).** More general (name,
  level, weekdays…), much more to query. Only the target changes what "done"
  means; other edits are cosmetic or already handled (archive = `ArchivedOn`).

## Consequences

- Pure, unit-tested functions (`Domain/TargetHistory.cs`) hold the rules.
- Phase 7 streaks/statistics must call `TargetOn` (or read `TargetHistory`),
  never `Target`, for past days.
- The table is synced like the others (ADR 0017).
