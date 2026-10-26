# ADR 0014 — Day queue = template + per-day delta

**Status:** Accepted · 2026-10-06 · Phase 6 (Durak A)
**Supersedes:** —

## Context

The Bugün screen (SPEC §6) shows one ordered list per logical day: template
routines, plan cards and one-off items. The user reorders it, removes items
"for today", postpones ("Sonraya at"), skips with a reason, and can push the
day's changes back into the template ("Bunu varsayılan yap"). The same day
must look identical on the Rutinler page and, later, to the streak engine
(Phase 7) and the phone (MOBILE.md).

Routine done-state already lives in Routines (`routines_entries`), and plan
progress lives in Plans. The Today module must not become a second source of
truth for either.

## Decision

- The **template is the source of what a day contains**. Which template a day
  uses is stored in Routines (`routines_day_instances`), so every module asks
  `IRoutinesReader.GetTemplateForDateAsync` and gets the same answer.
- The Today module stores **only the user's changes for that day** in one row
  (`today_queue_state`): saved order, removed keys, one-off items, done marks
  for plan/one-off items, postpone counts, started-at. An empty row means "the
  day is exactly its template".
- Items are addressed by a string key `<kind>:<legacyId>` (`routine:reading`,
  `plan:backend-plan`, `adhoc:<uuid>`), built from pack ids so a pack
  re-import does not break a saved order.
- A **pure function** (`TodayQueueBuilder.Build`) merges template + routines +
  delta into the queue: saved keys first, new items after in natural order,
  stale keys dropped, weekday-limited routines hidden (not deleted), all-day
  routines split into the side panel.
- Routine status is read from Routines; Today never writes routine entries.
  Skips (`today_skips`) and closures (`today_day_closures`) are Today's own data.
- "Bunu varsayılan yap" rewrites template entries through `IRoutinesWriter`
  and first appends the previous entry list to the activity log (no backup
  module yet — project rules "no silent data loss").

## Alternatives considered

- **Materialise the day as rows on first open** (copy template entries into
  `today_items`). Simple reads, but a template edit made at 09:00 would not
  reach a day opened at 08:00, and two copies of "what is in the day" drift.
- **Keep the order only in the browser.** Breaks the phone and the "one source
  of truth" rule; Phase 4 already removed localStorage for this reason.
- **Mirror routine done-state into Today.** Faster single query, but two
  writers for the same fact; automations (Durak B) would need to update both.

## Consequences

- A day's view is always computed; cheap because a day has 10–25 items.
- The merge rules are unit tested without a database (`TodayQueueBuilderTests`).
- JSON lists on one row mean last-write-wins for two concurrent edits of the
  same day; acceptable for one user, and the UI locks pending rows.
- Phase 7 streaks can read `GetTemplateForDateAsync` (rest days, "Hafif gün")
  and `ITodayReader.ListClosuresAsync` (energy) without touching Today tables.

## Decision changed — 2026-10-06 (end of Durak A)

**Plan cards now carry an explicit reference.** The first version let the UI
guess plan and card type from the `refId` suffix (`backend-algo` → `backend` +
`algo`). That rule is gone:

- A template entry carries `planSlug`, `cardType` (`main` | `module`) and
  `moduleInstanceId`; `moduleInstanceId` is required for `module` and not
  allowed for `main` (`TemplateEntryRules.ShapeError`). Schema:
  `docs/schemas/templates.schema.json`.
- The extractor writes the fields explicitly (the Grind 75 id comes from
  `roadmap.mjs` as `GRIND75_INSTANCE_ID`) and stops on a broken reference.
- `PackImporter` throws `InvalidDataException` for an unknown `planSlug` or
  `moduleInstanceId`; no template row is written.
- `TodayQueueBuilder` passes the reference through and hides an invalid entry
  instead of guessing. The UI picks the card body from the plan layout (`main`)
  or the module type (`module`).
- New columns are added by an idempotent `ALTER TABLE … ADD COLUMN`
  (`ModuleSchema.EnsureColumnsAsync`) and filled by re-importing the pack.

**Side fix:** the pack import rewrote template entries on every launch, which
silently undid "Bunu varsayılan yap". Templates now store the hash of the
pack's entry list (`PackEntriesHash`); entries are rewritten only when the
pack changes, and then the pack wins (it is the newer source).
