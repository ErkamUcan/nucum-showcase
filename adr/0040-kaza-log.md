# ADR 0040 — Kaza namazı: an append-only log in a Prayer module, no focus session, no XP

**Status:** Accepted · 2026-10-14 · Phase 17
**Related:** ADR 0017 (sync fields), ADR 0020 (XP ledger), ADR 0029 (replication), DECISIONS F133–F137

## Context

The user wanted a Kaza namazı module (off by default): counts per prayer,
"Kıldım" (−1), add, correct, a start from two dates, and a focus screen with
two buttons. The counts are private and will later be changed on the phone
too. Kaza must not become a game: no XP, no streak, no achievements. The
planned design (F133) made the focus screen an `ActivitySession` with
`CountsAsWork = false`, but the XP rules give focus XP to every finished
session, whatever its type.

## Decision

- **New module `Prayer`** (`/api/prayer`) with one synced table
  `prayer_kaza_entries`: prayer, delta, reason (`kilindi` / `eklendi` /
  `duzeltme` / `baslangic`), logical date, optional session id, `UndoOf`.
- **The count is the sum of deltas.** Typed counts write the difference
  (start while a prayer has no row, correction after). Undo is a new row
  with the opposite delta pointing at the first; one undo per row, never
  below zero. Pure rules in `KazaRules`, tested without a database.
- **The kaza focus screen is not a focus session** (F137): it is a screen
  over the same log; each "Kıldım" row carries the screen's session id and
  the summary is computed from those rows. So no XP, no work minutes, no
  conflict with a running pomodoro (one active session rule).
- The Vakitler sound file lives in the same module (`media/prayer/`): one
  file, replaced on upload, typed (mp3/ogg/wav) and capped at 10 MB.
- Not in search, share packs or logs (the dispatcher logs only message
  names); in backups and sync like every synced table.

## Alternatives

- **A "left" column per prayer** — simple, but two devices that change it
  overwrite each other under last-writer-wins; rows that are only added
  merge without conflict.
- **ActivitySession + an XP exception for one activity type** — touches the
  XP projection (ADR 0020) and its recalculation for one feature; a screen
  over the log keeps Gamification unaware of kaza.
- **Kaza rows in the Activity log** — every reader of the activity log
  (XP, statistics, streaks) would have to learn to skip them.

## Consequences

- History never disappears; the page can show what was prayed when.
- The pace ("bu hızla tahmini bitiş") starts at the first row (`since`).
- A future phone writes the same rows; nothing to merge by hand.
