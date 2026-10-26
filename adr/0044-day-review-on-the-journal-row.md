# ADR 0044 — The day review lives on the day's journal row

**Status:** Accepted · 2026-10-20 · Faz 18 B3c
**Related:** DECISIONS F148, F153, `backend/.../Journal/Application/DayReviews.cs`

## Context

Faz 18 adds an evening review: Ruh hali, Odak, Enerji, Disiplin, Manevi hal
(1–10) and a note. Two values already existed elsewhere: Günlük's mood on
`journal_entries`, and Günü kapat's energy (1–5) on `today_day_closures`.
The user asked for one source: "Değerlendirmeye taşı".

## Decision

The review is **columns on the journal row of that logical day** (the
oldest live row, the rule the page editor already uses). `Mood` is Ruh
hali; `ReviewFocus`, `ReviewEnergy`, `ReviewDiscipline`, `ReviewSpiritual`,
`ReviewNote` are new. `/api/reviews` reads and writes only these fields and
sits outside the journal PIN lock. Günü kapat's old energy moves over once,
doubled (1–5 → 2–10), guarded by `journal.reviewEnergyMoved`; the closure
keeps no energy from now on. The phone writes the same columns through its
replica.

## Alternatives

- **A `day_reviews` table:** clean, but Ruh hali would be a second copy of
  Günlük's mood, and the two would drift the first time one screen saved.
- **Reviews in Today (closures):** the closure exists only when the day is
  closed; a review filled later on Günüm would have no row.

## Consequences

- Günlük's mood and the review can never disagree: it is one column.
- The review needs no new sync table; the phone gets it with the page.
- The journal lock does not hide the five numbers (they are day facts on
  Günüm). The page's words, photos and voice stay locked.
- A day with a review but no writing has a journal row with an empty page;
  Anılar and search skip empty pages already.
