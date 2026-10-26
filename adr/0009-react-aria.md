# ADR 0009 — React Aria Components (not shadcn/Radix defaults)

Date: 2026-10-02 · Status: Accepted

## Context
Nücum's design (DESIGN.md) is strongly opinionated and specifically
avoids the shadcn/Radix "look": neutral cards, lucide icons, generic
dropdowns. At the same time, accessibility is a hard requirement
(keyboard, screen reader, focus ring, high-contrast).

## Decision
Use **React Aria Components (Adobe)**. The library ships headless,
accessible primitives (Button, Dialog, Modal, TextField, Tabs,
DatePicker, ComboBox, ListBox, Table) with correct ARIA, focus trap,
roving tabindex, keyboard shortcuts and i18n built in. We put our own
visual language on top via CSS tokens.

## Alternatives
- **shadcn/ui + Radix:** great primitives, but the ambient visual
  defaults ("that shadcn look") are hard to get out of your mind and
  show up in every generic template screen we have seen.
- **Radix Primitives directly:** fine, less a11y richness than Aria
  (fewer locales for date, no built-in ComboBox).
- **Build everything ourselves:** we did write Tabs ourselves in
  Phase 1 since the behaviour we need is tiny. For Dialog/DatePicker
  we take the Aria version — building accessible dialogs from scratch
  is where projects fail.

## Consequences
- (+) High a11y bar met out of the box.
- (+) i18n for date and number inputs across 34 locales.
- (+) Visual language is 100 % ours — no shadcn fingerprint.
- (−) `react-aria-components` is still evolving (occasional breaking
  changes on minor versions); pin to a known-good minor and bump
  deliberately.
- (−) Learning curve: data-attribute based styling (`[data-hovered]`,
  `[data-selected]`) rather than classes.
