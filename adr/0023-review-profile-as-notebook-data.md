# ADR 0023 — Leitner review: one engine, the notebook says what a card is

**Status:** Accepted · 2026-10-08 · Phase 8

## Context

SPEC §12 asks for Leitner review over the plan notebooks with two hard
rules: the user chooses which fields form the question side, and the
source counters keep their meaning. The two notebooks disagree on that
meaning: the Backend hata defteri shows "x kez tekrar edildi" (every look
counts) and its "Tekrar ettim" button only counts; the YDS hata defteri
shows "Tekrar çözüldü" (only a correct re-solve), and "Tekrar çözdüm" must
move the card up a box. Phase 9 will add user-defined notebooks.

## Decision

- **One Leitner engine** (`Collections/Domain/Leitner.cs`): 5 boxes,
  intervals from settings, "forgot" → box 1 and due tomorrow; every entry of
  a reviewable notebook is a card (`Box`, `DueOn` columns on the entry).
- **A review profile per notebook, as data**: fields in order, question
  fields, `countOn` (`any` | `remembered`), `manualAdvances`, HTML fields and
  `groupBy` (for the weekly "most frequent mistakes"). The extractor writes it
  into the pack's module settings; the pack importer stores it on the
  collection (`ReviewProfileJson`). The user's question-field choice lives in
  a separate column the pack never overwrites. No profile = not reviewed
  (YDS kelime stays in Anki).
- The interview deck (rule 3) gets its profile from app code, because it
  exists only because the rule exists.

## Alternatives

- **One bespoke review screen per notebook** — two now, N after Phase 9.
- **SM-2 (Anki's algorithm)** — an "ease factor" per card the user cannot
  see or reason about; overkill for hundreds of rows.
- **Infer fields from existing rows** — fails on an empty notebook and
  guesses the counter meaning.

## Consequences

- The Phase 9 notebook editor only has to write the same profile shape.
- Old rows need no backfill: no due date = due now.
- `countOn` is a small, explicit rule; a third meaning would extend the
  enum, not add a code path per notebook.
