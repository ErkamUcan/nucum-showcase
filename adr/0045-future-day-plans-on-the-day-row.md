# ADR 0045 — Plans for a future day wait on that day's row and only add

**Status:** Accepted · 2026-10-20 · Faz 18 B6
**Related:** DECISIONS F155, ADR 0038, `backend/.../Today/Application/FuturePlans.cs`, `mobile/src/today/future-plan.ts`

## Context

From the calendar the user can plan any later day: a template plus cards
from the pools, with "O gün otomatik uygula" or "O gün sor". By the time
the day comes it may already have cards (a routine added by hand, a
routine done early, the phone planned too).

## Decision

The plan is stored as `FuturePlanJson` on **that day's `today_queue_state`
row** — `{mode, status, templateId, items, adhoc}` — not in the day's card
lists. On its day:

- **auto:** the first read of the day (`GET /api/today/{date}`, or the
  phone opening Bugün) applies it;
- **ask:** Bugün shows "Bu gün için planın var · Uygula · Atla".

Applying **only adds**: a card already on the day is skipped, a one-off item
with the same title is skipped, new cards go to the end of the saved order,
and the template is only recorded as the day's template when the day has
none. The status becomes `applied` (or `skipped`), so a second apply adds
nothing. The Yarın step of Günü kapat stays the special case that writes
tomorrow at once (`PUT /plan`, a replace, ADR 0038).

## Alternatives

- **A `future_plans` table:** a new sync table for the phone, and orphan
  rows when a day is deleted.
- **Write the cards into the day at once:** "O gün sor" would be impossible,
  and changes made on the day would fight the plan.
- **A night job at the cutover:** nothing guarantees the app runs at 04:00.

## Consequences

- One more column; the phone syncs it with the rest of the day.
- A GET writes once (the auto plan). It is idempotent, so two devices
  applying at the same time are harmless (only-add + status).
- Template cards the user removed from the plan stay removed: the plan's
  list is the truth, the template is a label.
