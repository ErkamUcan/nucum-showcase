# ADR 0029 — Sync protocol: change log, HLC, field-level last-writer-wins

**Status:** Accepted · 2026-10-08 · Phase 10
**Supersedes:** ADR 0007 (server-authoritative command queue) — DECISIONS M4

## Context

The phone (React Native, post-Phase 12, MOBILE.md §5) has its own full
SQLite database and works alone. When the desktop is reachable, both sides
exchange changes in both directions. ADR 0007 assumed a PWA that only sends
commands to an authoritative server; that model breaks when the phone also
holds data the desktop never saw. ADR 0017 already gave every replicated row
a UUIDv7 `Id`, `UpdatedAtUtc`, `DeviceId` and a `DeletedAtUtc` tombstone.

Problems to solve: (1) what is "a change" on the wire, (2) which change is
newer when device clocks disagree, (3) a retried upload must not apply twice,
(4) concurrent edits of the same field, (5) deletes, (6) an old phone app
talking to a newer desktop.

## Decision

**Change record (one per written row).** `changeId` (UUIDv7, made by the
writer), `table`, `rowId`, `hlc`, `deviceId`, `fields` (the whole row:
property → JSON value) and `changed` (the property names this write really
changed). Sending the whole row makes every record self-contained: a device
that never saw the row can insert it; a device that has it applies only the
`changed` fields. Deletes are not a special operation: a delete is a change
of `DeletedAtUtc` (soft delete, ADR 0017), so it replicates like any field.

**Change capture = a transactional log in the same SaveChanges.**
`SyncStampInterceptor` (ADR 0017) now also writes a `sync_changes` row for
every added/modified sync-tracked row, in the same transaction as the row
itself. `sync_changes` is a SharedKernel table mapped into every module's
DbContext — the transactional outbox pattern: no row change without its log
entry, no log entry without the row. Its `Seq` (SQLite `AUTOINCREMENT`) is
the pull cursor; SQLite has one writer at a time, so a reader never sees
seq 11 committed while seq 10 is still pending (the "sequence gap" problem
of multi-writer databases does not exist here). Rows that existed before
Phase 10, or were inserted by raw SQL, get a baseline record at start-up.

**Time = Hybrid Logical Clock.** `hlc = (wallMs, counter, deviceId)`,
encoded as a fixed-width string so string order = clock order. Sending or
writing takes `max(local, wall)` (+1 counter on a tie); receiving takes
`max(local, remote, wall)`. So a change made after seeing another change is
always ordered after it, even when the phone's wall clock is behind. A
remote clock more than `sync.maxClockDriftMinutes` ahead of the desktop is
rejected (`clock_skew`), so one broken clock cannot drag every device into
the future.

**Push (device → desktop).** `POST /api/sync/push` with an
`idempotencyKey` for the batch and a `changeId` per change. A batch key seen
before returns the stored response; a `changeId` seen before is
`duplicate`. Apply is idempotent by construction too: a change whose HLC
equals the field's current version is a no-op, so a crash between "row
applied" and "inbox written" cannot double anything.

**Pull (desktop → device).** `GET /api/sync/pull?after=<seq>&limit=<n>`
returns log entries after the cursor (other devices' changes only), paged
(`nextCursor`, `hasMore`). The device stores `nextCursor` only after it has
applied the page. The desktop remembers each device's last pulled seq, so it
can show "N changes waiting".

**Conflicts: per field, last writer wins.** The desktop keeps every field's
current version implicitly: the newest log entry that changed that field.
Incoming field wins when its `(hlc, deviceId)` is greater. A push carries
`baseHlc` — the newest version of that row the device had merged when the
edit was made. If the field's current version is from another device and
newer than `baseHlc`, the two edits were concurrent: LWW still decides, and
the pair (winner, loser, both values) goes to `sync_conflicts`, shown on
Ayarlar → Telefon. Event-like rows (routine marks, activity sessions, sleep,
`activity_events`) are inserts with fresh UUIDv7 ids, so they never collide;
`activity_events` is declared **append-only** (insert if absent, never
updated).

**Natural keys.** Some live rows are unique by a key other than `Id`
(`settings.Key`, one row per day…). When an incoming new row clashes with a
live row on such a unique index, the desktop merges the incoming fields into
its row (field LWW), remembers the alias (`sync_id_aliases`), logs a
`natural_key` conflict, and answers `merged` with the desktop's row id so
the device re-keys its row.

**Schema version.** Every sync request carries `protocolVersion` and
`schemaVersion` (header `X-Nucum-Sync`). A mismatch answers **426** with a
ProblemDetails body (`code: sync.version_mismatch`, both versions, and which
side must update) — never a half-applied batch.

**Live update.** After a commit that wrote log entries, a debounced
notifier sends `changesAvailable(seq)` over SignalR to paired devices and to
the desktop UI (which refreshes its queries). It is a hint, not data: a
device that missed it still pulls on its own schedule.

## Alternatives considered

- **Keep ADR 0007's command queue.** Works when only the server holds data.
  The phone is a full peer here; replaying "commands" from the desktop to the
  phone would duplicate every handler on the phone.
- **Wall-clock `UpdatedAtUtc` for LWW.** Simple, but a phone whose clock is
  five minutes behind silently loses every edit it makes after a desktop
  edit. HLC keeps wall-clock-like values (readable, sortable) and fixes the
  ordering.
- **Vector clocks / CRDTs.** Detect concurrency exactly and can merge
  without loss, but cost per-field metadata for every device, and text CRDTs
  are a project of their own. Two devices + per-field LWW + a visible
  conflict log is enough for personal data.
- **Whole-row LWW.** Editing the name on the phone and the target on the
  desktop would lose one of them. Per-field costs only the `changed` list.
- **Log written after commit (separate connection).** Simpler interceptor,
  but a crash between the two writes loses a change forever.

## Consequences

- `sync_changes` grows with every write. Compaction (drop entries every
  paired device has pulled, keep the newest per field) is listed for
  Phase 12; at personal scale the table stays small for a long time.
- `ExecuteUpdate`/`ExecuteDelete`/raw SQL still bypass capture (ADR 0017
  rule). Raw-SQL inserts (Today's day row) are caught by the start-up
  baseline or by the next tracked update of the same row.
- Pushed rows are written directly; desktop handlers do not run for them.
  Projections that read `activity_events` incrementally (XP ledger) pick
  them up; per-handler side effects (automation rules for a phone mark) are
  a Mobile M1 question and are noted in the Phase 10 report.
- The phone must keep the same rules: UUIDv7 ids, HLC, the `changed` list,
  `baseHlc`, soft deletes. `tests/Nucum.FakeMobileClient` is the reference.

## Addendum — Phase 10 closing (2026-10-09, DECISIONS F47, F49)

- **Rules run only on the desktop.** A pushed row is state, not a command.
  After it is applied, the module that owns the table compares before/after
  (`ISyncApplyHook`) and raises the same domain event its own command would
  have raised: a routine entry flipping to done → `routines.completed`; a
  session gaining `EndedAt` → `focus.session_ended` + `ActivitySessionEnded`
  (routine credit, automation rules); a sleep time changing →
  `tracking.sleep_logged`. After the batch, `ISyncBatchHook` runs the
  Gamification catch-up (XP projection, achievements). The results are
  ordinary local writes, captured in the log, and come back on pull.
- **Idempotent by transition.** Only a change of state is an event; a
  duplicate or re-sent row with the same state triggers nothing, and the
  handlers keep their own guards (credit once, XP "seen" set, rule run keys).
- **Desktop-only tables.** `activity_events`, the XP ledger (append-only,
  now replicated to phones), achievements, unlocks and automation rules are
  written only by the desktop; a push into them is rejected
  (`desktop_owned`). The phone shows XP from pulled ledger lines and never
  computes it (offline: "pending").
- **"Use this value".** Restoring a conflict's losing value is a new local
  desktop change with a fresh HLC, so it wins on every device without any
  special undo message in the protocol. The same hooks run for it.
- Known gap: an XP rebuild (`ExecuteDelete` + replay) does not send
  deletions; line ids are deterministic, so a replay re-sends the same ids
  and only removed lines would linger on a phone. Revisit with compaction
  (Phase 12). → **Closed in Phase 12 by ADR 0035** (purge records + log
  compaction).
