# ADR 0025 — Recordings are files next to the database, not blobs

**Status:** Accepted · 2026-10-08 · Phase 8

## Context

The interview drill (SPEC §9.2) records optional spoken answers (a few
hundred KB each, Opus/WebM from MediaRecorder). project rules forbids browser
storage (localStorage / IndexedDB): it stays on one device, misses backups
and dies with the WebView. Phase 10 will receive photos and audio from the
phone, so the choice sets a pattern.

## Decision

- The page uploads the recording as the **raw request body** with its
  Content-Type to `PUT /api/drills/{id}/answers/{answerId}/recording`.
- The API writes `<data folder>/media/drills/<answerId>.<ext>` — the data
  folder is the SQLite file's folder (`NucumPaths.MediaDirectory`,
  overridable with `Nucum:Media:Directory`). Write to `.tmp`, then move.
- The database row keeps only the **file name**, mime type and length.
- Only known audio types (webm, ogg, mp4, mpeg, wav) and ≤ 20 MB are
  accepted; the file name is built from the answer id, never from input.
- Playback fetches the bytes with the Bearer token and plays a `blob:` URL.

## Alternatives

- **BLOB column** — the database (and every backup copy) grows with audio,
  row reads get heavy, SQLite page cache fills with media.
- **Base64 in JSON** — 33 % larger and the whole clip in one string.
- **Browser storage** — forbidden (see context).

## Consequences

- Backups (Phase 11) must copy `media/` next to the `.db` file.
- Sync (Phase 10) will move media as files with their own endpoint, keyed by
  the row id.
- Deleting a recording is an explicit user action and removes the file.
