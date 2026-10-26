# ADR 0004 — Own small CQRS dispatcher (no MediatR)

Date: 2026-10-02 · Status: Accepted

## Context
We want CQRS primitives (commands, queries, pipeline behaviours) so
handlers are small and cross-cutting concerns (validation, logging,
transaction, activity log) stay out of the business code.

## Decision
Write a **~80-line dispatcher** inside `Nucum.SharedKernel.Messaging`:
- `ICommand<T>`, `IQuery<T>`, `ICommandHandler<,>`, `IQueryHandler<,>`,
  `IPipelineBehavior<,>`.
- `Dispatcher` resolves the handler by reflection (`MakeGenericType` +
  `IServiceProvider`), wraps it in registered behaviours (reverse order),
  returns `Result<T>`.
- Handlers return `Result<T>` instead of throwing on expected errors.

## Alternatives
- **MediatR:** our prior default. Switched to a commercial licence in
  2024-11; we deliberately avoid commercial-licence libraries for a
  portfolio project.
- **MassTransit (Mediator variant):** also moved to a paid tier.
- **Direct `IService` classes:** blur the write/read split; cross-cutting
  code leaks into handlers.

## Consequences
- (+) No commercial dependency; license risk neutralised.
- (+) The dispatcher's implementation is a teaching artefact — I can
  explain every line in an interview.
- (+) Trivial to extend with new behaviours (e.g. ActivityLog, Outbox).
- (−) Reflection cost (~few µs/call). Measured; fine for this traffic.
  If it ever shows in a profile, cache the delegate per message type.
- (−) We own the bug surface. In exchange, no third-party upgrade cycle.

## Note — Phase 6.5 (2026-10-06)

Until Phase 6.5 the dispatcher existed but no endpoint used it (FEATURE_AUDIT
AR4.2). Now every module goes endpoint → command/query → handler, and the
implementation was fixed on the way: the dispatcher is **scoped** (it was a
singleton resolving scoped handlers), the handler call is **lazy** (it ran
before the behaviours, so validation came too late), and MethodInfo lookups
are cached per message type. Pipeline today: Logging → Validation
(FluentValidation, Apache-2.0) → handler. Results map to ProblemDetails in one
place (`ResultHttp`). Architecture tests (`LayerRulesTests`) keep it that way.
