// File summary: Command/Query marker interfaces and their handler contracts.
// Lives in SharedKernel.Messaging. The small dispatcher (Dispatcher.cs) finds
// the right handler at runtime. Concepts: CQRS, mediator pattern.
namespace Nucum.SharedKernel.Messaging;

using Nucum.SharedKernel.Primitives;

// CONCEPT: CQRS — Command Query Responsibility Segregation.
// Writes (commands) and reads (queries) are different in shape and
// optimisation. Commands mutate state and may raise events; queries never
// change state and are free to use denormalised read models.
// WHY: In Nücum, a routine completion (command) is cheap; the stats page
// (query) is cheap in a different way (hits a day-summary projection).
// Mixing them in a single "service" class muddles the two paths.
// GOTCHA: Nothing enforces purity for queries at compile time — do it in
// code review. A query that writes is a smell.
// INTERVIEW: "CQRS vs plain services?" → CQRS makes the two concerns
// explicit; it is not a different storage model (unless you also split).
// → Roadmap: Ay 2 · Faz 1 — CQRS & MediatR Alternatifleri
public interface ICommand<TResult> { }
public interface IQuery<TResult> { }

public interface ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken ct);
}

// CONCEPT: Pipeline behaviour.
// Each behaviour wraps the next in a Russian-doll fashion. Order is:
// Logging → Validation → Handler (Behaviours.cs). Transaction and
// activity-log behaviours from ARCHITECTURE §4.2 are not needed yet: each
// handler saves once, and the activity log is written by the handlers.
// WHY: Cross-cutting concerns (validation, logging) stay out of handlers.
// Each behaviour is testable alone and composed by registration order.
// → Roadmap: Ay 1 · Faz 3 — Middleware Pipeline
public interface IPipelineBehavior<TMessage, TResult>
{
    Task<Result<TResult>> HandleAsync(
        TMessage message,
        Func<Task<Result<TResult>>> next,
        CancellationToken ct);
}

// The dispatcher is the single entry point used by endpoints. Keeping it
// behind an interface lets integration tests fake it when needed.
public interface IDispatcher
{
    Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    Task<Result<TResult>> AskAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}
