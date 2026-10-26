# ADR 0020 — XP is an append-only ledger projected from the activity log

**Status:** Accepted · 2026-10-07 · Phase 7

## Context

SPEC §17 gives XP for completions (weighted by level, plan nodes, exams…),
a streak multiplier, levels and titles. Almost every write in the app can
move XP, and every write can be undone ("Geri al"). The activity log
(ADR 0005) already records what happened, append-only. A stored "total XP"
counter would have to be updated correctly by every module and every undo —
one bug and the number drifts forever.

## Decision

- **XP comes only from `activity_events`.** `XpRules.Apply(event, state)` is a
  pure function: one event → zero or more ledger lines.
- **Ledger, not counter.** `gamification_xp_ledger` holds append-only lines
  `{SourceEventId, LineNo, LogicalDate, SubjectKey, Kind, Amount}`. The total
  is `SUM(Amount)`.
- **Undo = reversal line.** Lines are keyed by WHAT was earned
  (`routine:<id>:<day>`, `plan:<slug>:<item>`, `focus:<session>`…). A
  completion awards once per key; the undo event adds a negative line equal
  to what is still credited. Duplicate completion events add nothing.
- **Events carry what the rule needs.** `routines.completed/uncompleted` now
  fire only on a real transition and carry the entry's own `date` and the
  routine's `level` (a past day filled in today is booked on that day; a
  later level change does not re-price old work). Older events fall back to
  the event's logical day and the routine's current level.
- **Streak multiplier through events.** The streak is computed from routine
  entries, not from the log, so a `DayEvaluator` writes the verdict of each
  finished day (`gamification.day_evaluated {date, kept, percent}`) into the
  log; the bonus line is that percent of the day's base XP and moves when a
  later verdict for the same day differs. Achievement unlocks also enter the
  log (`gamification.achievement_unlocked`) and pay by rarity.
- **Idempotent, rebuildable projection.** Line ids are a hash of
  (event id, line number); handled events are remembered in
  `gamification_xp_seen`. "XP'yi yeniden hesapla" deletes both projection
  tables and replays the whole log; the result is the same ledger
  (`XpProjectionTests`).
- **Catch-up on read.** `GamificationSync` brings day verdicts → ledger →
  achievements → ledger up to date before any gamification read, behind one
  in-process gate. No background timer.

## Alternatives

- **A `users.xp` counter updated by each module.** Simple to read; every undo
  path must remember to subtract; impossible to audit or rebuild.
- **Compute XP on every read from the whole log.** Always right, but O(log)
  per sidebar render and no place to keep per-line history ("what earned me
  this?").
- **Background worker projecting every N seconds.** The sidebar would lag
  behind a tick; tuning the interval is guesswork. Catch-up on read is
  always current.

## Consequences

- The ledger and `xp_seen` are projections: ADR 0017 exception list (each
  device rebuilds them). `SyncContractTests` lists them.
- Changing `xp.weights` affects new lines; "Yeniden hesapla" re-prices history.
- Two requests committing events out of time order can produce lines in a
  slightly different order than a rebuild; totals are equal (GOTCHA in
  `XpProjector`).
- Proven by `XpRulesTests` (award/undo/duplicate/old events/bonus/focus) and
  `XpProjectionTests` (tick → up, untick → down, rebuild twice = same rows).
