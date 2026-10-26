# ADR 0039 — Günlük: one page per day, block JSON, files on disk, a private index and a memory-only unlock key

**Status:** Accepted · 2026-10-12 · Phase 14
**Related:** ADR 0017 (sync fields), ADR 0029 (replication), MOBILE.md §4.6/§4.7, DECISIONS F92–F96

## Context

The user asked for a journal tab: a notebook page per day with text blocks,
photos, videos and voice notes, two optional 1–10 scores, a history to browse
and search, statistics on the scores, an optional PIN and an export. The phone
(post-Faz 12) will write the same journal, so the tables must sync and stay
small enough for a phone database. Media can be large (a 200 MB video).

## Decision

- **New module `Journal`** with two synced tables: `journal_entries` (one row
  per logical day; content = TipTap JSON, plus derived columns `PlainText`,
  `HasPhoto/HasAudio/HasVideo`, `FirstPhotoId` filled on every save) and
  `journal_media` (kind, type, size, file name, thumbnail, duration, waveform
  peaks). No unique index on the date: a soft-deleted row and an offline phone
  page may share a day; the oldest live row is "the" page.
- **Media are files, not BLOBs**, under `<data>/media/journal/`, named from the
  media id with an extension from an allow-list. Uploads are raw request bodies
  streamed to a temp file with a hard cap (`journal.videoMaxMegabytes`, 200),
  then moved. A phone's file is found through the Sync module's new
  `IPhoneMediaLocator` by the phone's media id (Faz 10 media upload), so the
  page shows desktop and phone media the same way. `<img>`/`<video>` load files
  with `?access_token=` (allowed only on these two GET routes) and stream with
  Range requests.
- **Search**: a private FTS5 table `journal_fts` (Turkish folding as the global
  index), rebuilt lazily when the page count or the newest `UpdatedAtUtc`
  changes — phone writes are picked up without an event. Ctrl+K also lists
  pages, but only while no PIN is set.
- **PIN**: PBKDF2 (SHA-256, 120 000 rounds, random salt) hash in the setting
  `journal.pinHash`, which the generic settings API never returns or writes.
  An unlock returns a random key kept in API memory and page memory only; while
  a PIN is set every journal route (except `/lock`) answers 423 without it.
  Leaving the tab revokes the keys.
- **Insights** reads only the two scores (`IJournalReader`), never the text.

## Alternatives

- *Store text in Thoughts/Collections*: the page is not a note list; scores,
  media and the one-page-per-day rule would be bolted on.
- *BLOBs in SQLite*: every backup and sync read would carry the media.
- *LIKE search*: no index, no accent folding.
- *Lock only in the UI*: the API would still serve the text to anything that
  can call it.

## Consequences

- Phone M-phases reuse the tables and the media path; the desktop does not yet
  send its own journal media to the phone (download route comes with M-phases).
- The FTS table is rebuilt whole when anything changed — fine for years of
  daily pages, not for millions of rows.
- The PIN is a privacy screen for one local user, not encryption at rest: the
  database file itself is not encrypted.
