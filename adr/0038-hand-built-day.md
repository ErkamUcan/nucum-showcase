# ADR 0038 — Bugün is built by hand; templates are copied in, from a cut-over date

**Status:** Accepted · 2026-10-12 · Phase 13 (Durak B)
**Supersedes:** ADR 0014 (day queue = template + diff) for days from `today.manualPlanSince` on
**Related:** ADR 0016 (one credit path), ADR 0018 (target history), DECISIONS F79–F82

## Context

Until Phase 13 a day was its weekday template plus the changes the user made
that day (ADR 0014). After daily use the user asked for the opposite: Bugün
opens **empty**, a one-line suggestion offers the weekday's template, any
template can be applied from Günler, routines are dragged in from a side
panel, and a routine ticked anywhere joins the day. Weekdays on a template
become a suggestion (and the plan calendar's input), never an automatic fill.

Streaks, karne, XP and statistics all ask "what did this day want?", and
they read the weekday template. Two years of history were computed that way.

## Decision

- **A day holds what the user put into it.** `today_queue_state.AddedJson`
  lists the items (routine keys; plan cards keep their template reference),
  `TimesJson` their optional clock times. One-off items stay in `AdhocJson`.
  "Çıkar" on a hand-built day removes the item from the list (adding it again
  is a plain add).
- **Apply = copy, not link.** Applying a template copies its routines and
  plan cards into the day (items already there are skipped, so applying
  twice adds nothing) and records the applied template for the day
  (`routines_day_instances`), so its rest-day flag still reaches streaks.
  Editing the template later never changes a day that used it.
- **Done anywhere joins the day.** Routines publishes `RoutineCompleted` on
  every done edge (tick, count, timer credit, goal mark, automation, phone);
  Today adds the routine to that day. Routines does not know Today exists.
- **Requirement = the day's content.** For a hand-built day the streak and
  statistics read `ITodayReader.GetDayPlansAsync` (wrapped as a template by
  `DayTemplateDto.FromDayPlan`). An empty day asks for nothing — neutral.
- **Cut-over date instead of a data migration.** `today.manualPlanSince` is
  seeded once with the first logical day this build runs. Earlier days keep
  the template model, so no past streak, karne or XP value moves.
- **Time order (rule B, F79)** is a pure function with a twin in TypeScript;
  both run `docs/schemas/today-time-order-cases.json`.

## Alternatives

- **Rewrite past days into the new shape.** Rejected: it would silently
  change history the user has already seen (streak lengths, achievements).
- **Keep the template as the source and auto-apply it.** Rejected: that is
  exactly what the user asked to stop.
- **Link the day to the template (no copy).** Rejected: editing a template
  on Günler would rewrite days already lived.

## Consequences

- Two day models live side by side, chosen by date. Tests pin
  `today.manualPlanSince` explicitly so they do not depend on the run date.
- "Bunu varsayılan yap" on a hand-built day writes the day back into the
  template the user applied (or is refused when none was applied).
- The all-day panel exists only for template days; on a hand-built day every
  item is in the one list.
