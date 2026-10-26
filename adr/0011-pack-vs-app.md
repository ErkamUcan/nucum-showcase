# ADR 0011 — Personal pack / app separation, Plan Format v1

Date: 2026-10-02 · Status: Accepted

## Context
The repository will be public (portfolio). The user's three source HTMLs
(backend roadmap, YDS plan, daily tracker) contain personal content:
names, routines, messages, book choices, exam date. That content must
not be committed, but the app must still boot on first run with those
real routines and plans.

## Decision
Two kinds of content, kept strictly apart:
1. **Application code** (generic): all modules, UI, logic. Public.
2. **Personal pack** (private): JSON produced by `tools/extract/` from
   `sources/`. Lives in `packs/personal/` (gitignored). First launch
   detects the pack and imports it idempotently via the `Packs` module.

A **Plan Format v1** (C# records + JSON Schema + TS types) is the only
protocol between the pack and the modules. The format is the stable
contract; the pack is just a serialisation of it.

`sources/` is read-only. The extractor is the **only** path from source
to pack — no hand-editing, no hand-rewriting of pack content. Fidelity
tests (ADR 0012) prove the extractor did not drop or alter content.

## Alternatives
- **Hard-code personal content:** can't open-source the repo.
- **Private submodule:** adds a second repo to manage and still mixes
  content with code at check-out time.
- **Database seed script:** fragile, hand-maintained, no fidelity test.

## Consequences
- (+) Repo can be public without exposing anything personal.
- (+) The pack format doubles as the import/export format for Phase 11.
- (+) Any user can author a pack (JSON) and import it — the generic
  "plan engine" promise.
- (−) Two artefacts to version (schema vs pack).
- (−) First-run onboarding must handle missing-pack gracefully; we add
  an EmptyState in Plans/Routines that explains how to generate one.
