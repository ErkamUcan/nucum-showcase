# ADR 0042 — The phone applies its colour pack before the screens load

**Status:** Accepted · 2026-10-19 · Mobile M11
**Related:** DECISIONS M48, DESIGN.md §3 (palettes), ADR 0041

## Context

The desktop has eight looks (colour packs and space looks) and switches
them live through CSS variables. The phone has no CSS: about 22 screen
files build their styles once with `StyleSheet.create({ … colors.ink … })`,
which copies the colour values at module load. The user wants the looks on
the phone as **the phone's own choice** (it does not change the desktop).

## Decision

- The choice is stored in the phone's own `app_meta` (`ui.palette`); it is
  never synced.
- `mobile/index.ts` opens the data once (`data/boot.ts`), reads the choice
  and fills the shared `colors` object with that pack (`applyPalette`)
  **before** `App` (and with it every screen file) is imported with
  `require`.
- A new choice in Ayarlar → Görünüm is saved and the app reloads with
  `reloadAppAsync()` (about a second), so every style is built again with
  the new values.

## Alternatives

- **A theme hook in every component** (`useTheme()` + styles made inside
  render): live switching, but every file changes, styles are rebuilt on
  every render and a forgotten file keeps the old colours.
- **React context with memoised style factories:** the same work in every
  file, only cheaper at run time.
- **Sync the choice with the desktop:** rejected by the user (the phone may
  want a different look than the PC).

## Consequences

- One place decides the colours; no screen code changed.
- Switching is not live: a short reload. It happens rarely (a settings
  choice), so this is acceptable.
- GOTCHA: anything that imports `theme` before `index.ts` applies the pack
  would keep the default colours, so `index.ts` must stay the only entry and
  import only `data/boot` and `theme/palette` before `App`.
