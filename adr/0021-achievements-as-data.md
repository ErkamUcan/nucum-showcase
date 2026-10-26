# ADR 0021 — Achievements are data, evaluated with the automation condition language

**Status:** Accepted · 2026-10-07 · Phase 7

## Context

SPEC §17 asks for 100 default achievements, an editor that builds new ones
"without code" (streak, total, count, first time, plan milestone,
combination), retroactive unlocking from past data, and no shower of
celebrations when many open at once. The automation engine (SPEC §14)
already has a condition shape (`RuleCondition` = all/any of
`{metric, op, value}` clauses).

## Decision

- **Conditions as data, one language.** An achievement's condition is a
  `RuleCondition`. The combine rule and the comparison operators moved into
  a pure `ConditionEvaluator` in `Automation.Contracts`; the automation
  engine and the achievement evaluator both call it and only differ in how
  they read one clause (today's work minutes vs. a lifetime fact).
- **Facts snapshot.** Before evaluating, `AchievementService` computes every
  lifetime number once (best streaks, totals, counts, plan percentages…)
  into `AchievementFacts`; each clause is a lookup. Parameters ride on the
  metric name: `streak.routine.best:<routine>`, `plan.percent:<slug>`,
  `focus.startedBefore:0700`.
- **Defaults are app content, texts are i18n.** The 100 conditions live in
  an embedded `default-achievements.json` (key, group, rarity, icon, hidden,
  chain); their Turkish names/descriptions live in
  `frontend/src/i18n/tr/achievements.json`. Not in the personal pack.
- **User achievements** (`gamification_achievements`, synced) carry their
  own name/description and the same condition shape; editable and soft-
  deleted.
- **Retroactive without a storm.** The first evaluation ever marks every
  already-earned unlock `Retroactive`; a new user achievement that already
  holds also opens retroactively. The UI shows retroactive or multiple
  unlocks as ONE summary card. Unlocks are never taken back.
- **Celebrations are server state.** Pending moments are unlock rows (and
  milestone rows) with `SeenAtUtc = null`; the client marks them seen.

## Alternatives

- **Hard-coded C# achievement classes.** Type-safe, but the editor would be
  impossible and adding one means a release.
- **A separate expression language (e.g. a mini DSL string).** More
  powerful; needs a parser, error reporting and an editor that writes code —
  the opposite of "kod yazmadan".
- **Per-achievement queries.** Simple to write; 100 conditions × clauses =
  hundreds of queries per tick.

## Consequences

- New fact = one line in `AchievementFacts` + one entry in the metric
  catalog; stored conditions never need a migration.
- `AchievementCatalogTests` (100 unique keys, valid chains/icons/metrics) and
  `constellation.test.ts` (every key has i18n text) keep the two halves in
  step.
- An accidental tick can open an achievement that stays open after the undo
  (XP of the routine is reversed; the unlock and its XP stay) — accepted.
