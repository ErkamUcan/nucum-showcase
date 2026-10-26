# ADR 0022 — XP calibration: a good day ≈ 100–150 XP, six weeks ≈ level 6–8

**Status:** Accepted · 2026-10-07 · Phase 7

## Context

The Phase 7 draft weights (level-A routine 30 XP, 10 XP per 30 focus
minutes, 25 % / 50 % streak bonus, level base 150) were picked one by one.
On the six-week fixture they added up to ~600 XP a day, ~24 000 XP and
**level 18** — the level box stopped meaning anything after a month, and
the title ladder (41 levels) would be used up within a year.

XP should be a quiet, slow signal (SPEC §17 "ince çubuk; dikkat dağıtmaz"),
so I set two targets first and derived the numbers from them:

1. **A good day is worth about 100–150 XP.**
2. **Six weeks of regular use land on level 6–8.**

## Decision

**Reference good day** (the unit the weights are tuned against): every
level-A habit (8), three of the B and one of the C habits, six hours of
work plus an hour of other focus, sleep logged, the day closed, a streak
in its second week.

| Source | Weight | Reference day |
|---|---|---|
| Level A / B / C routine | 7 / 4 / 2 | 56 + 12 + 2 |
| Focus, per full 30 min | 3 | 7 h → 42 |
| Close the day · sleep log | 5 · 2 | 7 |
| Plan block · today item · plan node · problem · exam | 3 · 4 · 25 · 10 · 30 | — (bonus days) |
| Achievement Dim / Bright / Shining / Legendary | 5 / 15 / 30 / 75 | — |
| **Base** | | **119** |
| Streak bonus ≥7 / ≥30 / ≥100 days | +10 % / +20 % / +30 % | +12 → **131** |

**Level curve:** level *n+1* needs `base · n(n+1)/2` XP in total (triangular,
unchanged shape), **base = 200**: level 2 at 200, 7 at 4 200, 8 at 5 600,
9 at 7 200. 42 days × 100–150 XP = 4 200–6 300 XP → level 7–8; a
slightly weaker six weeks (~95 XP/day) still reaches level 6.

**Formula in one line:**

```
dayXP   = Σ weight(completion) + 3·⌊focusMin/30⌋ + achievements
bonus   = round(dayXP_without_bonus · percent(streakLength))   (written by DayEvaluator)
level   = max n such that 200 · (n−1)·n/2 ≤ totalXP
```

**Old databases:** `XpCalibration` (Gamification start-up) replaces
`xp.weights`, `xp.streakBonuses` and `xp.levelBase` **only when they still
equal the draft defaults exactly** (a value the user changed is never
touched), then empties the XP projection; the next read replays the whole
activity log with the new prices (ADR 0020). Old `day_evaluated` verdicts
carrying the draft percent are superseded because `DayEvaluator` writes a
new verdict when the percent differs.

## Alternatives

- **Keep the weights, steepen the curve** (e.g. exponential). Level 8 would
  then mean ~24 000 XP; numbers in the hundreds per tick look like a game
  economy, not a calm habit tracker, and big totals hide small wins.
- **Daily XP cap.** Simple, but a truly great day would look the same as a
  good one, and the cap is one more setting to explain.
- **Normalise by the number of routines** (a day = 100 % → 120 XP). Fair
  across users, but adding a new habit would make every old one worth less
  — surprising, and it would re-price history.

## Consequences

- Weights are still user settings (SPEC §2); the targets hold for the
  defaults. "XP'yi yeniden hesapla" re-prices history after a manual change.
- A user with many more level-A habits earns more per day; that is
  intended (more commitments kept), and the level curve absorbs it slowly.
- Proven by `XpCalibrationRulesTests` (the reference day with the default
  weights is 100–150 XP; the curve maps 42 × 100…150 XP to level 6–8) and
  `XpCalibrationFixtureTests` (six weeks of the fixture history through the
  real API: level 6–8, median XP of kept days 100–150; observed: level 7,
  4 599 XP).
