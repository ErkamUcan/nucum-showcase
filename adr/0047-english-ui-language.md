# ADR 0047 — An English UI next to Turkish

**Status:** Accepted · 2026-10-26 · Phase 19 (desktop 0.19.0)
**Related:** DECISIONS F164–F171, `frontend/src/i18n/` (`language.ts`, `locale.ts`, `en/`), `packages/shared/coach`, `backend/.../Plans/Gallery/en/`

## Context

The app was Turkish only, but the i18n pipeline was there from Phase 1: every
visible word comes from `frontend/src/i18n/tr/*.json` and a test fails on text
written in code. The public README is read by people who do not read Turkish.
The phone keeps Turkish for now (a later mobile stage).

Three kinds of text are not plain UI copy:

- **Text that came from the user's source plans** (`tr/source.json`): button
  labels and messages of the Sleep / Work / Routines and plan pages. The rule
  is that this text is never rewritten; the fidelity tests compare it byte by
  byte with the sources.
- **Data**: plan content, routine names, the personal pack. It is the user's
  own words.
- **Content shipped with the app**: the plan template gallery, coach messages
  and quotes, the demo pack.

## Decision

- **One setting, `ui.language` = `system` | `tr` | `en`.** It is not seeded;
  absent means `system`, which follows the WebView's `navigator.languages`
  (WebView2 reports the Windows display language): a `tr*` language gives
  Turkish, anything else English. The value lives in the settings table like
  every other preference (no browser storage) and switches the UI live.
- **The client owns UI text.** `en/*.json` mirrors every `tr` namespace,
  and a parity test checks that both have the same keys and the same
  `{{placeholders}}`.
- **Source-plan text keeps its Turkish original.** `tr/source.json` is not
  touched; English mode reads an English counterpart in `en/source.json`.
  The user approved this on 2026-10-26.
- **Data is never translated.** A routine named "Okuma" stays "Okuma" in
  English mode.
- **Formatting follows the language**: one `uiLocale()` (`tr-TR` / `en-GB`)
  for dates, numbers and percentages. Formatters are created per call, not at
  module load, so a language switch reaches them. Search folding stays
  Turkish-aware because it folds data, not UI.
- **English plurals without touching call sites:** an i18next formatter
  `{{n, plural(one: day; other: days)}}` picks the form with
  `Intl.PluralRules`. Turkish files do not need it.
- **The server stays language-neutral**: errors are codes that the client
  translates. The client sends `Accept-Language`, and the few places that
  serve shipped content choose by it: the template gallery (`Gallery/en/`),
  plan module type descriptions, the review prompt and the journal export.
- **Shipped content gets English twins**: `packs/coach/messages.en.json`,
  `packs/quotes/general.en.json`, `packs/demo-en/`.

## Alternatives

- **Server-side resource files (`IStringLocalizer`) for all text:** the
  server already returns codes, and most text never touches it. Moving copy
  to the server would double the work and split it across two stacks.
- **`count`-based i18next plurals everywhere:** correct, but every call site
  that passes `n` or `days` would have to change. The formatter keeps calls
  as they are.
- **Leaving source-plan labels Turkish in English mode:** follows the rule
  literally, but gives a mixed-language Routines page.

## Consequences

- A new UI string has to be added to both languages; the parity test fails
  otherwise.
- User data stays in the language it was written in. A Turkish user who
  switches to English still sees their own Turkish routine names, which is
  expected.
- English screenshots need English data: the demo pack has an English twin.
