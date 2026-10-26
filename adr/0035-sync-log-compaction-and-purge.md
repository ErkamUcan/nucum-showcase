# ADR 0035 — Change-log compaction (newest row per field) and purge records

**Status:** Accepted · 2026-10-09 · Phase 12
**Related:** ADR 0029 (sync protocol), ADR 0020 (XP ledger projection), DECISIONS F48

## Context

`sync_changes` is the outbox every phone reads with a cursor (ADR 0029). It
grows with every write and never shrinks. Two readers still need old rows:

1. a phone paired later pulls from cursor 0 and rebuilds the database from
   the log alone;
2. the desktop's field merge (`FieldMerge.CurrentVersions`) takes the newest
   HLC of each field of a row from the log to decide last-writer-wins and to
   detect concurrent edits.

A second gap (ADR 0029 addendum): "XP yeniden hesapla" drops the XP ledger
with `ExecuteDelete` and replays the activity log. `ExecuteDelete` is invisible
to change capture, so a ledger line the replay no longer produces stayed on a
phone forever.

## Decision

- **Purge record.** For rows that disappear for real (today only the XP
  ledger, a projection), the writer adds a log row with `IsDelete = true` and
  an **empty** changed list (`SyncPurge.Add`). A soft delete always lists
  `DeletedAtUtc`, so the two cannot be confused. Pull marks it `Purged = true`;
  the phone removes its copy. `XpProjector.RebuildAsync` compares the line ids
  before and after the replay and purges the missing ones in the same
  transaction; lines that come back keep their deterministic ids and travel
  as ordinary inserts.
- **Horizon** = the smallest `PulledSeq` of the active (not revoked) phones;
  with no phone, the whole log is history. Only rows at or below it may go.
- **Rule (pure, `ChangeCompaction.Removable`):** per (table, row), newest
  first, a row at or below the horizon is removed when every field it changed
  was changed again by a later row. Everything older than a purge that is
  itself below the horizon goes, purge included. The insert row carries `Id`,
  which no edit repeats, so every live row keeps at least one log row and the
  start-up baseline never re-adds it.
- **When:** `SyncCompactionWorker` (hosted service) every
  `sync.compactEveryHours` (24), first run two minutes after start; also
  `POST /api/phone/compact`. Deletion is `ExecuteDelete` in chunks of 500
  (deleting log rows must not create log rows).

## Alternatives

- **Truncate everything every phone has read.** Breaks bootstrap from cursor 0
  and the merge's field versions (false conflicts or lost LWW decisions).
- **Snapshot table + truncate.** A separate "current state" export for new
  phones; more moving parts, and the merge would need a second source of
  field versions. Compaction keeps one source.
- **Soft-delete column on the ledger.** The ledger is rebuilt from scratch;
  a tombstone would have to survive the rebuild that removed its row.

## Consequences

- Field versions after compaction are exactly the ones before it, so merge
  decisions and conflict detection do not change (`SyncCompactionTests`).
- A phone that never comes back holds the horizon down; revoking it in
  Ayarlar → Telefon releases the history.
- Restoring an old backup can put the log behind a phone's cursor; the phone
  must then pull again from 0. Detecting that (a log "epoch") is a Mobile M1
  item; the desktop side is unchanged by this ADR.
- The inbox (`sync_inbox`) and stored batch answers (`sync_batches`) are not
  compacted yet; both are tiny next to the log.

## Interview angle

"Your outbox grows forever — how do you shrink it without breaking new
consumers?" → delete only what every consumer acknowledged, and compact
(newest record per key) instead of truncating when consumers bootstrap from
the log — the same idea as a Kafka compacted topic.
