// File summary: The small CQRS dispatcher. Resolves the handler for a given
// command/query from DI, composes the pipeline behaviours around it, invokes
// it, returns a Result<T>. Replaces MediatR (which went commercial in 2024).
// Lives in SharedKernel.Messaging. Every module endpoint calls it.
namespace Nucum.SharedKernel.Messaging;

using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nucum.SharedKernel.Primitives;

// GOTCHA: The dispatcher is SCOPED, not a singleton. Handlers use a scoped
// DbContext; a singleton dispatcher would resolve them from the root
// provider and keep one DbContext alive for the whole app (a "captive
// dependency"). Before Phase 6.5 it was a singleton — nobody called it, so
// the bug never showed.
// INTERVIEW: "What happens if a singleton depends on a scoped service?" →
// the scoped service lives as long as the singleton (captive dependency).
// → Roadmap: Ay 1 · Faz 1 — Dependency Injection — Yaşam Döngüleri
internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    // CONCEPT: MakeGenericType + runtime handler lookup.
    // I know the message type only at runtime, so I build the generic
    // handler type with reflection and resolve it from the container.
    // WHY: Lets me pass ICommand<T> as plain interface at call sites while
    // the dispatcher finds the concrete handler for me.
    // GOTCHA: Reflection costs a few microseconds per call, so the
    // MethodInfo lookups are cached per (message type, open handler) pair.
    // INTERVIEW: "Why not source generators?" → Overkill for this scale and
    // it would mean fewer things to show in code — this project is also a
    // teaching artefact.
    // → Roadmap: Ay 2 · Faz 2 — Reflection & Source Generators
    private static readonly ConcurrentDictionary<(Type Message, Type Open), (Type Handler, MethodInfo Handle, Type Behaviour, MethodInfo BehaviourHandle)> Cache = new();

    public Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        => InvokeAsync<TResult>(command, typeof(ICommandHandler<,>), ct);

    public Task<Result<TResult>> AskAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        => InvokeAsync<TResult>(query, typeof(IQueryHandler<,>), ct);

    private async Task<Result<TResult>> InvokeAsync<TResult>(object message, Type openHandler, CancellationToken ct)
    {
        var messageType = message.GetType();
        var info = Cache.GetOrAdd((messageType, openHandler), key =>
        {
            var handlerType = key.Open.MakeGenericType(key.Message, typeof(TResult));
            var behaviourType = typeof(IPipelineBehavior<,>).MakeGenericType(key.Message, typeof(TResult));
            return (handlerType, handlerType.GetMethod("HandleAsync")!, behaviourType, behaviourType.GetMethod("HandleAsync")!);
        });

        var handler = services.GetRequiredService(info.Handler);

        // GOTCHA: `next` must be LAZY. The first version started the handler
        // task before the behaviours ran, so a validation failure came too
        // late — the handler had already written to the database.
        Func<Task<Result<TResult>>> next = () => (Task<Result<TResult>>)info.Handle.Invoke(handler, [message, ct])!;

        // Pipeline behaviours wrap the call. I walk them in reverse so the
        // first registered behaviour is the outermost.
        var behaviours = services.GetServices(info.Behaviour).Cast<object>().Reverse().ToList();
        foreach (var b in behaviours)
        {
            var current = next;
            next = () => (Task<Result<TResult>>)info.BehaviourHandle.Invoke(b, [message, current, ct])!;
        }
        return await next().ConfigureAwait(false);
    }
}

public static class DispatcherServiceCollectionExtensions
{
    // Called once by the host. Registration order = pipeline order:
    // Logging is outermost (it also times validation), Validation runs
    // right before the handler.
    public static IServiceCollection AddNucumDispatcher(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }

    // Called by every module's RegisterServices. Picks up the module's
    // command/query handlers and its FluentValidation validators by
    // convention, so adding a use case never touches the module file.
    public static IServiceCollection AddHandlersFromAssemblyOf<TAnchor>(this IServiceCollection services)
    {
        var asm = typeof(TAnchor).Assembly;
        var openTypes = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IValidator<>) };
        foreach (var type in asm.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var i in type.GetInterfaces().Where(i => i.IsGenericType && openTypes.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(i, type);
            }
        }
        return services;
    }
}
