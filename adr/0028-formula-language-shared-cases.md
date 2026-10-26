# ADR 0028 — Own formula language, implemented twice, one test file

**Status:** Accepted · 2026-10-08 · Phase 9

## Context

Kayıt/Sayaç and Deneme takibi need formulas (SPEC §8.1:
`net = doğru − yanlış / 4`). The server must validate formulas in plan files
and AI answers (with an error the person understands); the screen must show
values live while typing.

## Decision

- A tiny recursive-descent language: numbers, field keys (Turkish letters
  allowed), `+ − × ÷` (typographic signs too), parentheses, `min`, `max`,
  `abs`, `round`. No variables other than fields, no strings, no code.
- Blank field = 0; all fields blank = no value; division by zero = no value;
  `round` is away-from-zero on both sides.
- Implemented in C# (`SharedKernel/Formulas/Formula.cs`, validation) and in
  TypeScript (`modules/engine/formula.ts`, evaluation on screen). Both run
  the same vectors: `docs/schemas/formula-cases.json`.
- A computed field may use only number/computed fields **above** it — no
  cycles by construction. Values are never stored; they are derived on read.

## Alternatives

- **Evaluate on the server only** — every keystroke a round trip; charts
  would need a server endpoint per view.
- **A JS expression evaluator / `eval`** — runs code; unacceptable for
  content that comes from AI answers and shared files.
- **An expression library (NCalc, mathjs)** — larger surface than needed,
  English errors, and still two implementations.

## Consequences

- A change to the language must change both files and the shared cases;
  CI fails on whichever side drifts.
