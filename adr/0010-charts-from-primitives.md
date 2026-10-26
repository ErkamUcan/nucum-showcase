# ADR 0010 — Charts built from d3 primitives (not Recharts/Chart.js)

Date: 2026-10-02 · Status: Accepted

## Context
DESIGN.md §10 explicitly forbids the default look of chart libraries
(Recharts, Chart.js, nivo). All charts must sit in the same visual
family as the rest of the UI: soft curves (`curveMonotoneX`), rounded
caps, dashed target lines, token colours, no legends, no tooltips with
drop shadows.

## Decision
Build small, SVG-based chart components (`LineChart`, `Ring`,
`Sparkline`, later `BarGroup`, `WeekRibbon`) using only **d3-scale** +
**d3-shape**. d3 ships the maths and geometry; the drawing stays as
plain SVG so we control every pixel.

## Alternatives
- **Recharts/Chart.js/nivo:** fast to get a chart on screen, but the
  default theme is immediately recognisable and theming them out of it
  is more work than drawing SVG from scratch.
- **Observable Plot:** nicer API than Recharts, but the ambient look is
  still visible; also a bigger dependency than we need.

## Consequences
- (+) Visual language is 100 % Nücum.
- (+) Bundle size tiny — only the two d3 submodules we actually use.
- (+) Component API narrow and typed, matching our data shapes.
- (−) Advanced interactions (tooltips, brush, zoom) take work. We add
  them per screen only when the screen needs them.
- (−) Accessibility of charts is bespoke; we annotate with
  `role="img"` and summary text per DESIGN.md §11.
