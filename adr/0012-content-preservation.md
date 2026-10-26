# ADR 0012 — Content preservation: programmatic extraction + fidelity tests

Date: 2026-10-02 · Status: Accepted

## Context
The three source HTMLs are the final arbiter of the user's content:
exact wording of messages, exact numbers (counts, thresholds), exact
tone (emoji, `<code>`/`<b>` tags, dash types, quote types). The project
has an absolute rule (project rules): never hand-rewrite, summarise,
translate, or "fix" any of that content.

## Decision
1. **Only** the extractor (`tools/extract/`) moves content from
   `sources/` to `packs/personal/`. It operates with jsdom and
   sanitize-html; no model-driven rewriting of strings.
2. **Fidelity tests** (`tests/fidelity/`) count every message pool,
   assert every link, count every section, and diff the extracted
   strings byte-for-byte against the source where applicable.
3. The fidelity suite is part of CI; a failing fidelity test blocks
   release.
4. `sources/` has read-only ACLs locally and is gitignored; the
   extractor only reads.

## Alternatives
- **Hand-write the pack:** forbidden by the project's charter. Low
  signal-to-noise; errors are irreversible.
- **Let the AI generate the pack from text summaries:** worst case —
  silently drops or paraphrases.
- **Snapshot the whole DOM:** can't be re-used by other users of the
  app; also massive.

## Consequences
- (+) The user can trust that no personal content was changed.
- (+) Extractor is a single, testable program.
- (+) Fidelity tests are the strongest signal we can give in a code
  review that no content drifted.
- (−) Writing a correct extractor is more work than a one-off script;
  budgeted for Phase 2.
- (−) The fidelity suite needs maintenance when a source document
  changes; we document the "update sources + rerun extract + rerun
  fidelity" loop.
