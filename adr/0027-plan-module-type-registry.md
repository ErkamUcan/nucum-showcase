# ADR 0027 — Plan-module types: a backend registry + a frontend manifest

**Status:** Accepted · 2026-10-08 · Phase 9

## Context

SPEC §8 asks for a plan engine made of modules (Katman 1: Konu takibi,
Defter, Kayıt/Sayaç, Deneme, Kanban, Analiz, Geri sayım…) and for coded
modules added "çekirdeğe dokunmadan" (§8.2, ARCHITECTURE §5). Before Phase 9
module types were string checks scattered over Packs, Today and the pages.

## Decision

- **Backend:** `SharedKernel.Modules.IPlanModuleTypeRegistry` merges the
  descriptors every module gives through `IPlanModuleTypeProvider`: id,
  owning module, storage (PlanItems / Collection / Notes / None / Module),
  Bugün card yes/no, selectable in the wizard, settings JSON Schema (shown in
  the AI prompt), a `Validate` function, the item kind of content modules and
  the data fields an "Analiz" may chart. A type registered twice fails at
  start-up.
- Plans owns roadmap, checklist, topics, countdown, analysis, notes;
  Collections owns notebook, log, exam, kanban (one notebook per instance,
  slug = instance id, prepared on the `PlanContentSaved` event); the example
  extension `DailyPick` owns `daily-pick`.
- **Frontend:** `modules/engine/registry.ts` — every module exports a
  `NucumUiModule` manifest (tab view, Bugün card body, settings editor,
  defaults for a new instance); `modules.ts` registers them. The plan page,
  editor, wizard and Bugün only ask the registry.
- The **validator** (`PlanValidator`) is generic over the registry: unknown
  type, bad settings, wrong item kind, analysis pointing at a missing field
  are all found through descriptors.

## Alternatives

- **One big switch per screen** — every new module edits core files.
- **Runtime plugin loading (AssemblyLoadContext, dynamic import)** — not
  needed for a single-user desktop app; build-time registration keeps types
  checked. The descriptor contract would not change if it came later.
- **A JSON Schema library for settings** — another dependency; the useful
  checks (formula names, cross-field rules) need code anyway.

## Consequences

- Adding a module: new projects + one line in `ModuleHost` + one import in
  `modules.ts` (docs/MODULE_GUIDE.md). Core screens stay unchanged.
- The Backend/YDS bespoke pages still exist (pack layouts `roadmap`,
  `day-list`); their module types are registered as not selectable.
