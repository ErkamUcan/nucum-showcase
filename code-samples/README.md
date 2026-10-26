# Code samples

Nineteen files copied **unchanged** from the private Nücum repository. They were picked to show the ideas the main [README](../README.md) talks about, in the code that actually runs.

Every file opens with a summary, and the important places carry tagged comments — `CONCEPT:` (what this is), `WHY:` (the decision), `GOTCHA:` (the trap), `INTERVIEW:` (how to explain it in one breath). Reading the comments alone is a fair tour of the design.

## Backend — the request pipeline

| File | What to look at |
|---|---|
| [`Messaging/Contracts.cs`](backend/src/Nucum.SharedKernel/Messaging/Contracts.cs) | `ICommand` / `IQuery` and their handlers; the pipeline-behaviour contract |
| [`Messaging/Dispatcher.cs`](backend/src/Nucum.SharedKernel/Messaging/Dispatcher.cs) | A ~100-line mediator: closed generic handler types built by reflection and cached, behaviours wrapped in reverse order |
| [`Messaging/Behaviours.cs`](backend/src/Nucum.SharedKernel/Messaging/Behaviours.cs) | Logging and FluentValidation behaviours; validation failures never reach the handler |
| [`Primitives/Result.cs`](backend/src/Nucum.SharedKernel/Primitives/Result.cs) | `Result<T>` and `Error` — expected failures as values, implicit conversions keep handlers tidy |
| [`Messaging/ResultHttp.cs`](backend/src/Nucum.SharedKernel/Messaging/ResultHttp.cs) | The only place that knows HTTP status codes; RFC 9457 ProblemDetails |

## Backend — modular monolith

| File | What to look at |
|---|---|
| [`Modules/INucumModule.cs`](backend/src/Nucum.SharedKernel/Modules/INucumModule.cs) | The whole module contract: register services, map endpoints, migrate |
| [`Modules/ModuleSchema.cs`](backend/src/Nucum.SharedKernel/Modules/ModuleSchema.cs) | Many `DbContext`s, one SQLite file: idempotent per-module DDL and an index "desired state" sync |
| [`ArchitectureTests/LayerRulesTests.cs`](backend/tests/Nucum.ArchitectureTests/LayerRulesTests.cs) | NetArchTest + reflection: peers only via `.Contracts`, endpoints inject only `IDispatcher` (read from compiler-generated lambda methods), handlers only in `Application` |

## Backend — data, events, projections

| File | What to look at |
|---|---|
| [`Persistence/SyncFields.cs`](backend/src/Nucum.SharedKernel/Persistence/SyncFields.cs) | A `SaveChanges` interceptor that stamps rows, turns deletes into tombstones and writes the change log in the same transaction; a global query filter built as an expression tree |
| [`Events/DomainEventPublisher.cs`](backend/src/Nucum.SharedKernel/Events/DomainEventPublisher.cs) | In-process domain events — and an honest `GOTCHA` about what happens without an outbox |
| [`Gamification/.../XpProjector.cs`](backend/src/Modules/Gamification/Nucum.Modules.Gamification/Application/XpProjector.cs) | A deterministic, idempotent projection from an append-only log; catch-up on read with a safety window; streak verdicts written back as events |

## Sync — desktop and phone

| File | What to look at |
|---|---|
| [`Sync/Hlc.cs`](backend/src/Nucum.SharedKernel/Sync/Hlc.cs) | Hybrid Logical Clock with a fixed-width text form whose string order equals clock order |
| [`Sync/Domain/FieldMerge.cs`](backend/src/Modules/Sync/Nucum.Modules.Sync/Domain/FieldMerge.cs) | Per-field last-writer-wins as a pure function, plus "was it really concurrent?" |
| [`Sync/Application/SyncApplier.cs`](backend/src/Modules/Sync/Nucum.Modules.Sync/Application/SyncApplier.cs) | The push side: idempotency key → inbox → equal-HLC no-op; device and clock-skew checks; natural-key merge |
| [`mobile/src/sync/client.ts`](mobile/src/sync/client.ts) | The phone's sync round: deterministic batch keys, cursor moved only after a page is stored, error kinds for 401/426 |
| [`mobile/src/sync/hlc.ts`](mobile/src/sync/hlc.ts) | The same clock in TypeScript, tested against the same rules as the C# version |

## Security and the edges

| File | What to look at |
|---|---|
| [`Infrastructure/ExtensionScope.cs`](backend/src/Nucum.Api/Infrastructure/ExtensionScope.cs) | Least privilege without OAuth: one method + path allow-list for the browser extension's token |
| [`frontend/src/api/client.ts`](frontend/src/api/client.ts) | A shell adapter: the same React code runs in Tauri, a plain browser and a browser extension |
| [`tools/check-personal.mjs`](tools/check-personal.mjs) | A data-driven leak check: the words to guard are read from the git-ignored personal data, never written in code |

<sub>These files are shared to show how the project is built. All rights reserved.</sub>
