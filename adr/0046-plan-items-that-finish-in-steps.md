# ADR 0046 — Plan items that finish in steps

**Status:** Accepted · 2026-10-25 · Backend plan v2 (desktop 0.18.2)
**Related:** DECISIONS F159–F162, `backend/.../Plans/Application/ItemSteps.cs`, `PlanProgress.cs` (`SetStepAsync`), `frontend/src/modules/plans/backend-steps.ts`

## Context

The v2 curriculum of the Backend plan has items that are not a single tick: a
roadmap node is finished when its three skills are marked, and an algorithm
question when its four steps are. Every screen that counts progress already
reads `plans_progress.DoneAt`: the sidebar %, statistics, XP, streaks,
achievements, the phone's plan sheet and the sync hook.

## Decision

- The step list for an item kind comes from the **plan's settings**
  (`{"steps": {"<kind>": [{"key", "label", …}]}}`), which the extractor writes
  from the source. There are no step names in code.
- The marks are stored in `plans_progress.DataJson` as `{"steps": [...]}`, in plan order.
  Keys the plan no longer defines are dropped.
- `DoneAt` is **derived**: it is set when the last step is marked and cleared
  when one is taken back. XP and the `PlanItemCompleted` event take the same
  path as a plain done (once on finish; "undone" on taking a step back).
- `PUT /api/plans/{slug}/items/{legacyId}/steps/{step}` toggles one step.
  An unknown step, or a kind without steps, answers 400.
- A row keeps living while it holds steps or the "Çözüme baktım" flag.
- **A plain tick stays valid.** The phone's plan sheet and Bugün's "bitti"
  only set `DoneAt`. A done row with no stored steps counts as **every step
  marked**, both on the API (`ItemSteps.Marked`) and on the page
  (`markedSteps`). So marking a step on it keeps it done, and taking one back
  reopens it with the other marks.

## Alternatives

- **A separate steps table and a new "finished" rule in every consumer:**
  more exact, but every progress reader, the sync tables and the phone would
  change. Rejected for the size of the change.
- **The phone and Bugün write all steps on a tick:** the phone build would
  change (new APK) and an old phone would still write plain ticks. The read
  rule covers both, with no phone change.

## Consequences

- Old consumers keep working unchanged. The finer state is only read by the
  Backend page.
- A plain tick cannot say *which* steps were done. It is shown as all of them.
- A curriculum that changes the step list re-derives the marks on the next toggle.
