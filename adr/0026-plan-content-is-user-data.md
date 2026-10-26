# ADR 0026 — Plan content is user data: sync fields, diff save, revisions

**Status:** Accepted · 2026-10-08 · Phase 9

## Context

Until Phase 8 plan content (plans, sections, items, blocks, module
instances, message pools) came only from the personal pack and was rewritten
by a re-import ("delete children, insert again"). ADR 0017 therefore listed
those tables as "not synced". Phase 9 adds the wizard, the plan editor, AI
import and plan files (SPEC §8.3): content is now typed by the person, must
travel to the phone later (MOBILE.md §5), must never be lost, and SPEC §3.5
asks for unlock → edit → revision history → "Orijinale dön".

## Decision

- The six content tables become **sync-tracked** (`UpdatedAtUtc`,
  `DeviceId`, `DeletedAtUtc`, soft delete, query filter) through
  `SyncSchemaUpgrade` on start-up. Only `plans_revisions` stays local.
- A whole document is written as a **diff by id**
  (`PlanDocumentStore.ApplyAsync`): same id → update, new id → insert,
  missing id → soft delete; a tombstone with a returning id is revived.
  Progress, notes and Bugün cards keep pointing at the same items.
- Every save, restore and pack re-import first stores the current document
  as a **revision** (Plan Format v1 JSON, `SnapshotFormat = 2`). Each plan
  has exactly one `original` snapshot: what the pack, the wizard, the AI or
  the file gave. "Orijinale dön" works even while the plan is locked.
- Saving requires an **unlocked** plan (409 `plan_locked`); a new plan from
  the wizard/AI/copy starts unlocked, a shared file keeps its own lock flag,
  pack plans keep `locked: true`.
- A pack re-import never overwrites a plan the user edited
  (`UserEditedAt`); it only replaces the `original` snapshot.
- New plans are **re-keyed** (fresh UUIDv7 for every section, item, block
  and module instance; references and `settings.source` rewritten), because
  gallery templates and AI answers reuse ids like `s1`.

## Alternatives

- **Keep delete-and-insert** — breaks progress links and cannot travel as
  sync tombstones.
- **Event-sourced plan edits** — exact history, but a large model for a
  document edited a few times a week; snapshots are enough to undo.
- **Copy-on-write plan versions (one row set per version)** — every reader
  would need "which version?"; revisions as JSON keep readers unchanged.

## Consequences

- `SyncContractTests` no longer lists the content tables as exceptions.
- Revisions grow with every save (one JSON per save); acceptable for
  personal plans, a trim job can come with Backup (Phase 11).
- Editing a pack plan's HTML in the editor goes through TipTap, which may
  normalise markup; the original text always returns with "Orijinale dön"
  and the fidelity tests check the pack files, not the database.
