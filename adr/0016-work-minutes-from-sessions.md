# ADR 0016 — Work minutes come only from activity sessions

**Status:** Accepted · 2026-10-06 · Phase 6 (Durak C)
**Supersedes:** —

## Context

The source tracker stored work as a number per day (`S.work[date] = minutes`)
and auto-ticked "Mesleki çalışma" at `backendMin`. Nücum now has the focus
timer (Durak B), whose `ActivitySession` rows already know how long and on
what the user worked, and a routine credit that adds session minutes to the
linked duration routine. Adding a separate "work minutes" table, or letting
the Çalışma page, automations and the timer each write routine minutes,
would create several truths for one fact and double-count minutes.

## Decision

- **Çalışma is derived.** Daily work minutes = Σ `ActiveMinutes` of the
  day's finished, not-deleted `ActivitySession`s (all types; per-plan
  breakdown through the type's `planSlug`). There is no work table.
- **History is edited through sessions.** "Geçmiş düzenleme" on the Çalışma
  page = add a `source=manual` session, correct a session's minutes, or
  soft-delete a session.
- **One credit path.** Minutes reach a routine only through the
  `ActivitySessionEnded` domain event → `CreditRoutineOnSessionEnded`
  handler. Manual add and correction raise the same event. Correction and
  delete first take back the session's current credit (subtract exactly what
  the handler wrote), so the routine total always equals the sum of the live
  sessions. A credit the user undid with "Geri al" is never brought back.
- **Automations mark, they never add minutes.** Rule 1 ("günlük çalışma ≥
  backendMin → Mesleki tamam") sets an automatic mark (`AutoDoneBy`) on the
  routine entry; it does not touch `Minutes`.

## Alternatives considered

- **Keep `S.work` as a table** (1:1 with the source). Simple import, but the
  timer would have to write it too → two sources; editing one would not fix
  the other.
- **Automations add minutes** (e.g. "mark Mesleki = set 360 minutes"). Fakes
  data: the routine would show minutes nobody worked.
- **Recompute routine minutes from sessions on every read.** Always correct,
  but it would erase minutes typed by hand into the routine dialog, which are
  a legitimate second input (not "work").

## Consequences

- Proven by `SingleCreditPathTests.Routine_minutes_always_equal_the_sum_of_live_sessions`
  (manual add, repeated finish, correction, delete, undo) and
  `AutomationTests` (the rule marks without minutes).
- The legacy import (Phase 5) turns each `S.work[date]` into one manual
  session on that date — the same path as the UI.
- "Çalışma" now means all focused work, not only Backend; the thresholds
  (`backendMin`, `backendGreat`) apply to that daily total, and the plan
  breakdown shows where the minutes went.

## Decision changed — 2026-10-06 (Phase 6 closing)

**"Work" is no longer every focused minute.** `ActivityType` has a
`CountsAsWork` flag; the pack default is `true` only for Backend.

- The Çalışma total, its thresholds (`backendMin` / `backendGreat`), its
  "üst üste 6s+" streak and its averages use only sessions of types with
  `CountsAsWork = true`. The per-plan breakdown still shows every type.
- Rule 1's `work.minutes` uses the same definition. Rule 2 ("YDS odak süresi
  ≥ 90 dk") now uses a separate `focus.minutes` metric (every session of the
  YDS plan), because YDS does not count as work.
- The flag can be switched per type in Ayarlar; once the user changes it, a
  pack re-import no longer overwrites it (`CountsAsWorkSetByUser`).

**Why:** in the source the threshold is `backendMin`, a *Backend* minimum.
With every session counted, 2 hours of a reading practice plus 4 hours of Backend reached
6 hours and auto-marked "Mesleki çalışma (Backend)" — reading and other practice must
not fill the Backend minimum. Proven by
`CountsAsWorkTests.Reading_120_plus_Backend_240_is_240_work_minutes_and_rule_1_does_not_fire`.

The single credit path (sessions → domain event → routine minutes) is
unchanged.
