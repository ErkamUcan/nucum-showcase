# ADR 0015 — A focus session belongs to the logical day it started on

**Status:** Accepted · 2026-10-06 · Phase 6 (Durak B)
**Supersedes:** —

## Context

Activity sessions (ADR-less model from MOBILE.md §3, DECISIONS M5) carry a
`LogicalDate`. Days turn over at the user's cutover hour (ADR 0006, default
04:00). A late study session can start at 02:30 and end at 05:10, crossing
the cutover. Routine credit (SPEC §13), the Çalışma page (Durak C), streaks
(Phase 7) and the phone (MOBILE.md) all group sessions by logical day, so
the rule must be one, simple and identical everywhere.

## Decision

- A session's `LogicalDate` is the logical day of its **start instant**
  (`ILogicalDateService.ToLogicalDate(StartedAt)`), stored on the row.
- A session that crosses the cutover is **not split**. All of its active
  minutes (and its routine credit) go to the start day.
- Correcting a forgotten start ("I started 20 minutes ago") recomputes the
  logical day from the new start.
- Duration is always derived from timestamps (start, end, pause intervals);
  there is no stored running counter.

## Alternatives considered

- **Split at the cutover** into two sessions / two credits. Accurate per-day
  hours, but one sitting becomes two rows, "Bitir" has to fan out, the undo
  of a credit touches two days, and a 03:50–04:10 session gives a 10-minute
  credit to a day the user already closed. Too much machinery for a rare case.
- **Assign to the end day.** The day is unknown while the session runs, so
  the live UI could not show which day it belongs to; and an idle timer left
  overnight would move yesterday's work into today.
- **Assign by majority of minutes.** Same unknown-while-running problem.

## Consequences

- A night owl's 02:30–05:10 work counts for "yesterday", which matches how
  the user thinks about that night (the cutover exists for exactly this).
- Per-day totals can include a little time after the cutover; reports should
  say "sessions started on this day".
- Tested in `ActivitySessionAggregateTests.A_session_crossing_four_am_stays_on_the_day_it_started`.
