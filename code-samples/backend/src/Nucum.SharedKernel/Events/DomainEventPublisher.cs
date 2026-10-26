// File summary: In-process domain event publishing. A service saves an
// aggregate, then hands its DomainEvents to IDomainEventPublisher, which
// finds every IDomainEventHandler<T> registered in DI and runs them in the
// same request scope. First used by the Focus module (ActivitySessionEnded →
// credit the linked routine). Lives in Nucum.SharedKernel.Events.
namespace Nucum.SharedKernel.Events;

using Microsoft.Extensions.DependencyInjection;
using Nucum.SharedKernel.Entities;

// CONCEPT: Domain event handler.
// A handler reacts to something that already happened ("a focus session
// ended") without the code that raised it knowing who listens. Focus raises
// the event; today Focus itself credits the routine, in Durak C Automation
// will listen to the same event — no change in the session code.
// INTERVIEW: "Domain event vs integration event?" → a domain event is raised
// inside one model and handled in-process; an integration event crosses a
// process/module boundary, usually through an outbox and a bus.
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent evt, CancellationToken ct);
}

public interface IDomainEventPublisher
{
    // Publishes and clears the aggregate's pending events.
    Task PublishAsync(AggregateRoot aggregate, CancellationToken ct);

    // Publishes one event raised outside an aggregate (e.g. a plan day that
    // just completed — Plans has no aggregate root for progress rows).
    Task PublishAsync(IDomainEvent evt, CancellationToken ct);
}

// GOTCHA: Events are published AFTER SaveChanges, in-process, without an
// outbox (ADR 0005 outbox is still pending). If a handler throws after the
// commit, the event is not retried automatically. Handlers must therefore be
// idempotent and the raising command must be safe to repeat (Focus "Bitir"
// re-publishes a session's end when its routine credit is still missing).
public sealed class DomainEventPublisher(IServiceProvider services) : IDomainEventPublisher
{
    public async Task PublishAsync(AggregateRoot aggregate, CancellationToken ct)
    {
        var pending = aggregate.DomainEvents.ToList();
        aggregate.ClearDomainEvents();
        foreach (var evt in pending) await PublishAsync(evt, ct);
    }

    public async Task PublishAsync(IDomainEvent evt, CancellationToken ct)
    {
        // CONCEPT: Closed generic lookup at runtime.
        // The event's runtime type picks IDomainEventHandler<ThatType>;
        // `dynamic` would also work but hides errors until runtime anyway,
        // so I resolve the closed generic explicitly and call it through
        // a tiny non-generic adapter.
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(evt.GetType());
        foreach (var handler in services.GetServices(handlerType))
        {
            if (handler is null) continue;
            var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            await (Task)method.Invoke(handler, [evt, ct])!;
        }
    }
}

public static class DomainEventServiceCollectionExtensions
{
    public static IServiceCollection AddNucumDomainEvents(this IServiceCollection services) =>
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
}
