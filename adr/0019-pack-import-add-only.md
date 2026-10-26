# ADR 0019 — The pack only adds; the user owns existing rows

**Status:** Accepted · 2026-10-07 · Phase 6.5 (Durak B)
**Changes:** DECISIONS P2 (template entries were rewritten when the pack's list changed)

## Context

The personal pack is imported on every launch. Until Faz 6.5/B, routines
could not be edited in the app, so a changed pack simply overwrote the
routine row; template entries were rewritten when the pack's entry list hash
changed (P2). Now routines and templates are edited, archived and deleted in
the app. Overwriting on re-import would silently undo the user's work, and a
deleted template would come back on the next launch (the soft-delete filter
hides the tombstone, so the importer saw "no such template").

## Decision

- The pack may **add** a routine or template whose `LegacyId` the database
  has never seen. It never updates an existing row and never revives a
  deleted (tombstoned) one; archived routines stay archived.
- "Never seen" is checked with `IgnoreQueryFilters()`, so tombstones count.
- Rows keep `UserEditedAtUtc` (routines) as a record of the user's ownership;
  `PackEntriesHash` (templates) now only records the original pack list.
- User-made rows get `u-<uuid>` LegacyIds, which never clash with pack ids.

## Alternatives

- **Update when the user has not edited the row** (per-row "touched" flag).
  Keeps P2's "a newer pack wins" for untouched rows, but two rules are harder
  to predict than one; the user chose the single rule.
- **Three-way merge** (pack old / pack new / user). Powerful, complex, and not
  needed for a single-user starter pack.

## Consequences

- A pack fix to an existing routine (e.g. a corrected target) no longer
  reaches an existing database; the user edits it in the app. Phase 9's demo
  and other packs follow the same rule.
- Proven by `PackAddOnlyTests.Reimport_never_overwrites_a_user_edit_or_revives_a_deleted_template`.
