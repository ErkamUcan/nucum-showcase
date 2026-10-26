# ADR 0017 — Sync fields, soft delete and their exceptions

**Status:** Accepted · 2026-10-06 · Phase 6.5 (Durak A)
**Supersedes:** — (prepares ADR 0007's revision, DECISIONS M4)

## Context

The phone app (MOBILE.md §5) works offline and syncs both ways with the
desktop; conflicts are settled by "last writer wins". That needs, on every
replicated row: a device-made id, when it last changed, which device changed
it, and a tombstone instead of a hard delete. Before Phase 6.5 only the
Focus tables had this (as `UpdatedAt` / `UpdatedByDeviceId` / `DeletedAt`);
Plans, Routines, Today and Settings had none, four tables were keyed by a
date or a key string, and several services deleted rows for real.

## Decision

- **Contract (`ISyncTracked`):** `Id` (UUIDv7, made on the device) +
  `UpdatedAtUtc` + `DeviceId` + `DeletedAtUtc`. The old Entity-base names
  were renamed to these (`UpdatedAt → UpdatedAtUtc`, `UpdatedByDeviceId →
  DeviceId`, `DeletedAt → DeletedAtUtc`).
- **One writer for the fields:** `SyncStampInterceptor` (SaveChanges
  interceptor) stamps `UpdatedAtUtc` and `DeviceId` on every added/modified
  row and turns `Remove()` into an UPDATE that sets `DeletedAtUtc`.
- **Soft delete is global:** `ApplySyncConventions()` adds a
  `DeletedAtUtc IS NULL` query filter to every synced entity and makes every
  unique index partial (`WHERE "DeletedAtUtc" IS NULL`), so a tombstone never
  blocks a new live row with the same key.
- **Device id:** created once per installation and kept in the device-local
  `settings_device` table; Settings (the first module to migrate) loads it
  into the `DeviceIdentity` singleton.
- **Upgrade without data loss:** `SyncSchemaUpgrade` runs before
  `EnsureTablesCreated`: column renames, added columns with a backfill
  (`UpdatedAtUtc` from an existing timestamp where one exists), and a
  transactional table rebuild for `settings`, `routines_day_instances`,
  `today_queue_state` and `today_day_closures` (date/key primary key → UUIDv7
  `Id` + unique live key). Index definitions are synced (changed ones are
  recreated, stale `IX_` ones dropped).
- **Id type:** Guid for the Guid tables (stored as upper-case text, as EF
  writes it); the older string-id tables (`plans_progress`, `plans_state`,
  `collections_*`, `tracking_metrics`…) already held UUIDv7 text and keep it.

### Exceptions (not synced)

| Table | Why |
|---|---|
| `activity_events` | Append-only log: rows are only inserted, never changed or deleted. Has `Id` (UUIDv7), `DeviceId`, `OccurredAtUtc`; syncs by insert only. |
| `activity_day_summaries` | Projection. Each device rebuilds it from the event log. |
| `automation_runs` | Append-only engine log ("this rule fired on this day"); written only by the desktop engine. |
| `plans_plans`, `plans_sections`, `plans_items`, `plans_blocks`, `plans_module_instances`, `plans_message_pools` | Pack content with deterministic ids (`itm-ay1-t1`). Both devices import the same pack and get the same rows; the pack (and `SourceHash`) is what syncs. Revisit in Faz 9 when plans become editable. |
| `plans_revisions` | Device-local snapshot taken before a re-import (a local undo, not shared data). |
| `settings_device` | This installation's own id; syncing it would make the phone adopt the desktop's id. |
| `outbox_messages` (future, ADR 0005) | A local delivery queue; each device has its own. |

`tests/Nucum.IntegrationTests/SyncContractTests.cs` holds the same list and
fails when a new table neither follows the contract nor is listed here.

## Alternatives considered

- **EF Core Migrations** for the upgrade. Cleaner tooling, but the app runs
  six DbContexts on one file with the `CREATE … IF NOT EXISTS` approach
  (ModuleSchema); switching mid-project would need a baseline migration per
  context. Kept for Faz 12 if the schema keeps changing.
- **Keep the date/key primary keys and add a unique `Id` column.** No
  rebuild, but the database would then disagree with the model on fresh
  installs vs upgraded ones.
- **Hard delete + a separate "deleted ids" table.** Two writes per delete
  and one more thing to sync; the tombstone on the row is simpler.

## Consequences

- Deleted rows stay as tombstones; tables only grow. A compaction step
  (hard-delete tombstones every device has seen) belongs to Faz 10.
- `ExecuteDelete`, `ExecuteUpdate` and raw SQL bypass the interceptor; they
  are only allowed on the non-synced tables above (today: the plan content
  re-import) or must set the fields themselves (Today's day-row upsert).
- Code that must see tombstones says so with `IgnoreQueryFilters()`
  (importers that respect a user's deletion, sleep "revive").

## Update — Phase 9 (2026-10-08)

Plan content (`plans_plans`, `plans_sections`, `plans_items`, `plans_blocks`,
`plans_module_instances`, `plans_message_pools`) is no longer an exception:
the wizard, the editor and AI import make it user data. Only
`plans_revisions` stays device-local. See ADR 0026.
