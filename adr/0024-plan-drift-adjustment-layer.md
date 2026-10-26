# ADR 0024 — Plan drift: one pace function, remedies as an undoable layer

**Status:** Accepted · 2026-10-08 · Phase 8

## Context

SPEC §8.4 wants a warning when a plan with a target date falls behind
("Sınava 40 gün var, plan 45 gün kaldı"), three remedies, a preview, and
"onaylanırsa içerik değişmeden takvim kayar". Phase 7 already said "bu hızla
sınavdan X gün sonra bitiyor" on the statistics page with its own pace
formula (always recent / 4 weeks). Two formulas would show two different
numbers for the same plan.

## Decision

- **One pure pace function** (`PlanDrift/Domain/PlanPace.cs`): pace over
  the last 28 days or the plan's real age (min 7 days); a day plan without
  history assumes its own pace (7 a week). Insights' plan forecast calls the
  PlanDrift reader, so both screens print the same finish date.
- **Remedies never edit the plan.** An applied remedy is a row in
  `drift_adjustments` (merged day pairs, or target before/after). The
  schedule = plan content + the active rows. Undo marks the newest row
  undone (and restores the exam date for "hedef tarihi kaydır").
- **Preview and apply run the same server code**; apply recomputes from the
  current data instead of trusting the client's preview.

## Alternatives

- **Rewrite the plan's day numbers / move blocks** — breaks locked content
  (SPEC §3) and fidelity tests, and undo would need a full copy.
- **Keep two pace formulas** — the exact inconsistency F4 (karne) fixed in
  Phase 7.
- **Store a per-day calendar** — 59 rows to keep in sync with every tick;
  the layer of pairs is smaller and derived.

## Consequences

- PlanDrift is an extension module; Insights depends on its Contracts only.
- A merged pair stops saving a day once one of its days is done — the
  saving was used (or lost), and the pace takes over.
- New remedies are new kinds producing pairs or a target move; the storage
  shape does not change.

## Closing amendment (2026-10-08)

The calendar is the plan's own working days: a date counts only when the
day's İdeal Gün template carries the plan's card (`PlanCalendar`, a pure
date → Off / Normal / Rest function built from the templates). Pace is
measured per working day, the finish date is found by walking working days,
and a merged pair may only share a Normal day — never a rest or light
template. Without this, "Salı · Tatil" made a plan on schedule read as late.

