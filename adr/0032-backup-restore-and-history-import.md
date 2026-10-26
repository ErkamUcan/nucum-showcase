# ADR 0032 — Backups are online SQLite copies in a zip; imports are history, written once

**Status:** Accepted · 2026-10-09 · Phase 11
**Related:** ADR 0003 (SQLite), ADR 0017 (sync fields, soft delete), ADR 0025 (media on disk), ADR 0029 (ISyncStore), DECISIONS P13

## Context

SPEC §21 asks for a daily automatic backup (last N kept), "back up now",
restore with a preview and a safety backup first, export of all data (JSON)
and of tables (CSV, Excel), and a JSON/CSV import that shows a dry-run report
and never creates duplicates. The app keeps the SQLite file open all the
time (EF connection pool, WAL mode) and part of the data is not in the file:
interview recordings and phone media live in `<data>/media` (ADR 0025).

## Decision

- **A backup is one zip**: `nucum.db` (made with `VACUUM INTO`, a consistent
  copy while the app runs), the whole media folder and `manifest.json`
  (kind, time, logical day, rows per table). Written as `.tmp`, renamed when
  complete. Kinds: `auto`, `manual`, `pre-restore`, `pre-import`.
- **Daily automatic backup** = a hosted service that checks every
  `backup.checkMinutes` whether *today's logical day* has an `auto` backup.
  Only `auto` backups are pruned (`backup.keepCount`, default 30); manual and
  safety copies are never deleted by the app.
- **Restore** = safety backup → `SqliteConnection.BackupDatabase` copies the
  backup's pages into the live database → clear the connection pool → swap
  the media folder (old one moved aside until success) → run every module's
  migration again (`IModuleMigrator`) so an older backup gets today's schema.
- **Export/import know no module**: they read and write rows through
  `ISyncStore` (ADR 0029). "All data" = the replicated tables; device
  bookkeeping (pairings, change log, revisions) is not exported.
- **Import is history, not events.** Rows are inserted *if missing*; nothing
  is updated. An Id comes from the file, or — for rows without one — from a
  SHA-256 of table + values (UUID version 8), so the same file twice adds
  nothing. Natural keys (one entry per routine per day) also count as "already
  here". No rule (automation, routine credit, XP for the act) runs for an
  imported row; only the XP capture runs, because it is a projection of the
  activity log and safe to repeat. A module can adjust its rows on import
  (`IDataImportPolicy`): Focus sets `Source = import` on sessions (MOBILE §3).
- **Dry run and apply share one code path** (`AnalyzeAsync`); apply takes a
  `pre-import` backup and records the written ids so "Geri al" can soft-delete
  exactly those rows.

## Alternatives considered

- **File copy of `nucum.db`.** Unsafe while writing; misses pages still in the
  WAL file.
- **Restore by replacing the file and restarting the API.** Needs the Tauri
  shell to restart the sidecar; on Windows an open file cannot be replaced.
- **Import through each module's commands (rules run).** A session from a file
  would credit today's routine and give XP again for minutes the imported
  routine rows already hold; history is not "something happening now".
- **Upsert on import (last value wins).** Could silently overwrite the user's
  newer data with an old export.

## Consequences

- One zip restores everything, media included; restore is itself reversible.
- A restored database can be older than what a paired phone has pulled; the
  phone keeps its cursor. A full re-pull after restore is a Faz 12 / mobile
  item (noted in the Phase 11 report).
- Excel export is a hand-written OOXML writer: values only, no styling.
