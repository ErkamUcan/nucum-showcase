# ADR 0041 — The phone keeps a generic replica; rules stay on the desktop

**Status:** Accepted · 2026-10-15 · Mobile M1–M6
**Related:** ADR 0029 (sync protocol), ADR 0030 (pairing), ADR 0035 (compaction), DECISIONS F47, M3–M5

## Context

The phone (React Native + Expo, `mobile/`) must work alone and sync with the
desktop in both directions (MOBILE.md §5). The desktop already speaks the
protocol of ADR 0029: change records carry the whole row (`fields`) plus the
names that changed, versions are HLCs, merges are per field
(last-writer-wins). `tests/Nucum.FakeMobileClient` was the reference client.
Open questions: how the phone stores the desktop's ~40 synced tables, how
it builds Bugün offline, and what it may compute itself.

## Decision

- **One generic replica table.** `replica_rows(tbl, id, data JSON,
  versions JSON, seen, deleted)` holds every row exactly as the desktop
  sends it, with an HLC per field. Local writes update the row and append
  to `sync_outbox` in the same SQLite transaction. Screens query with
  `json_extract` (an expression index on the logical date covers "one
  day"). No per-table schema, no phone migrations: a new desktop column
  simply appears in the JSON and is sent back untouched.
- **Same rules as the fake client**: UUIDv7 ids, HLC (TypeScript port of
  `Hlc.cs`, same text form), `baseHlc` = newest merged version,
  idempotent push (batch key + change ids), cursor moved only after a page
  is stored, purge records drop rows, `merged` re-keys the local row.
- **Natural-key inserts send only the fields they mean.** For tables with
  one live row per key (routine entry per day, queue state per day, sleep
  per wake date, settings key) a phone insert lists only the fields the
  user set in `changed`. If the desktop made the same row meanwhile, the
  merge cannot overwrite the desktop's other columns with phone defaults;
  locally those columns have no version, so the desktop's values replace
  them on the next pull.
- **Rules stay on the desktop (F47).** The phone writes marks, sessions,
  sleep, journal pages; it never writes XP, unlocks, routine credit
  minutes or automation results. To show them offline the desktop now
  replicates two snapshots as settings rows: `routines.messagePools` (the
  pack's celebration texts) and `gamification.phoneSummary` (level, XP,
  streak), refreshed after every phone push (`XpSyncBatchHook`). Timer
  minutes the desktop has not credited yet are shown as "bekliyor", never
  written.
- **Bugün is a port, not an API call.** `TodayQueueBuilder`,
  `RoutineDays.IsActiveOn`, `TargetHistory.TargetOn`,
  `RoutinesWriter.IsCountDone/IsDurationDone` are ported to TypeScript
  (`mobile/src/today/rules.ts`); logical-day and time-order helpers moved to
  `packages/shared/day` and are used by both apps.
- **Schema version 2.** New phone tables and columns on the desktop
  (`focus_pomodoro_modes`; activity `Category`, `ItemsJson`; session
  `QuantitiesJson`; thought `Kind`, `TagsJson`, `AttachmentsJson`;
  `thoughts_versions`; `thoughts_reminders`) bump `SyncProtocol.SchemaVersion`
  to 2, so an old phone or desktop gets 426 instead of a half-understood
  batch.
- **Files.** Phone media upload in 1 MB resumable chunks with a streamed
  SHA-256 (ADR 0029 media). Desktop-side files reach the phone through a new
  device route `GET /api/sync/files/{source}/{id}`; modules plug in with
  `IDeviceFileSource` (the journal does), so the Sync module still depends on
  no other module.

## Alternatives considered

- **A typed copy of every desktop table (Drizzle, MOBILE.md §2 draft).**
  Type-safe queries, but every desktop column change would need a phone
  migration, and a field the phone does not know would be lost when it
  writes the row back. The generic replica cannot lose fields.
- **Ask the desktop's query endpoints (Bugün, summary) when online.** The
  phone must work offline; two different code paths for "online" and
  "offline" would show different days.
- **Compute XP and streaks on the phone.** Faster feedback, but two engines
  would disagree (F47); the phone shows the desktop's numbers.

## Consequences

- Phone queries are JSON queries; fine for one person's data (thousands of
  rows), and covered by node:sqlite tests that use the same SQL.
- The ported rules must change with their C# twins; each TS function names
  its source, and `npm run mobile:sync-check` runs the phone's client against
  a real desktop API (pairing, merge, re-key, credit, media round trip).
- A `merged` re-key does not rewrite foreign keys in other rows. Only tables
  without incoming references are merged by key (settings, entries, queue,
  sleep, celebrations); the phone's "free focus" activity type uses a
  per-device `LegacyId` so it can never merge.
