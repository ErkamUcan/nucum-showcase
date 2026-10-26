// File summary: The two pipeline behaviours every command/query passes
// through: LoggingBehavior (name, time, failure code) and ValidationBehavior
// (FluentValidation rules → Error.Validation before the handler runs).
// Lives in SharedKernel.Messaging; registered by AddNucumDispatcher.
namespace Nucum.SharedKernel.Messaging;

using System.Diagnostics;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Nucum.SharedKernel.Primitives;

// CONCEPT: Logging behaviour (cross-cutting concern).
// One place logs every use case: which message, how long, and whether it
// failed. Handlers never write "Handling X…" lines themselves.
// WHY: Serilog request logging only shows the URL; this shows the intent
// ("TickRoutine failed: not_found in 3 ms").
// GOTCHA: Never log the whole message — it can carry the user's notes.
// Only the type name and the error code go to the log.
// → Roadmap: Ay 2 · Faz 3 — Logging & Serilog
internal sealed class LoggingBehavior<TMessage, TResult>(ILogger<LoggingBehavior<TMessage, TResult>> logger)
    : IPipelineBehavior<TMessage, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TMessage message, Func<Task<Result<TResult>>> next, CancellationToken ct)
    {
        var name = typeof(TMessage).Name;
        var started = Stopwatch.GetTimestamp();
        var result = await next();
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsSuccess)
        {
            logger.LogDebug("{Message} handled in {ElapsedMs:0.0} ms", name, elapsedMs);
        }
        else
        {
            logger.LogInformation("{Message} failed with {ErrorCode} in {ElapsedMs:0.0} ms", name, result.Error.Code, elapsedMs);
        }
        return result;
    }
}

// CONCEPT: Validation behaviour (FluentValidation).
// A command can have an AbstractValidator<TCommand> next to its handler. The
// behaviour runs every validator for the message type; if any rule fails,
// the handler is never called and the caller gets Error.Validation (HTTP
// 400 via ResultHttp). Messages without validators pass straight through.
// WHY: Input rules ("energy must be 1-5", "title is required") are not
// business logic. Keeping them declarative makes the handler read like the
// use case only, and the rules are unit-testable alone.
// GOTCHA: Validators check the SHAPE of the input. Rules that need the
// database ("does this routine exist?") stay in the handler and return
// Error.NotFound — a validator should not hit the DB.
// INTERVIEW: "Where do you validate in CQRS?" → shape at the edge (pipeline
// behaviour), invariants in the domain, existence in the handler.
// → Roadmap: Ay 2 · Faz 1 — CQRS & MediatR Alternatifleri
internal sealed class ValidationBehavior<TMessage, TResult>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TMessage message, Func<Task<Result<TResult>>> next, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var outcome = await validator.ValidateAsync(message, ct);
            failures.AddRange(outcome.Errors);
        }
        if (failures.Count == 0) return await next();

        // The first message becomes `detail`; every failure is listed under
        // `errors` (property → messages), the same shape ASP.NET uses for
        // ValidationProblemDetails.
        var errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => (object?)g.Select(f => f.ErrorMessage).ToArray());
        return new Error(ErrorKind.Validation, "validation", failures[0].ErrorMessage,
            new Dictionary<string, object?> { ["errors"] = errors });
    }
}
