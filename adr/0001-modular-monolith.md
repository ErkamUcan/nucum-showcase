# ADR 0001 — Modular monolith (not microservices)

Date: 2026-10-02 · Status: Accepted

## Context
Nücum is a single-user desktop app with a phone client. Future work (new
modules, phone offline queue, achievements rules) is additive. There is
no team to split along service lines and no need to scale a hot module
independently of the others.

## Decision
We build the backend as a **modular monolith**: one process, one deploy,
but strict internal boundaries. Each feature lives in its own project
(`Nucum.Modules.<Name>`) with a `*.Contracts` project that is the only
surface its peers may reference. A build-time test (`ModuleBoundaryTests`
in NetArchTest) fails if a module reaches into another module's internals.

## Alternatives
- **Microservices:** too much ceremony (IPC, config, deploy) for one
  developer and one laptop. Enables scale we do not need.
- **Folder-based modules in one project:** no compile-time enforcement.
  "Modules" degrade into folders inside a year.

## Consequences
- (+) Clear seams; refactor-friendly; one process to run and debug.
- (+) Can split a module into a service later without rewriting business
  code — the boundary is already drawn.
- (−) The architecture test must be maintained; every new module adds one
  more assembly to the dependency graph.
- (−) In-process coupling via DI must be resisted; cross-module calls go
  through Contracts or integration events, not direct repository access.
