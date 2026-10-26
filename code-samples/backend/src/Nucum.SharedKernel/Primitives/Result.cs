// File summary: Result<T> + Error — the return type every command/query handler
// uses instead of throwing. Lives in Nucum.SharedKernel.Primitives. Called by:
// every Application handler, every endpoint (which maps it to ProblemDetails
// through ResultHttp — the one place that knows HTTP status codes).
// Concepts: Result pattern, functional error handling.
namespace Nucum.SharedKernel.Primitives;

// CONCEPT: Result pattern (railway-oriented programming).
// Instead of raising exceptions for *expected* failures (validation errors,
// "not found", concurrency conflicts), the handler returns a Result that is
// either Success or Failure. Exceptions stay for truly unexpected bugs.
// WHY: Exceptions as control flow are costly (stack walks) and hide the
// shape of the API. With Result, the compiler forces me to handle the
// failure path at the call site — no silent swallowing.
// GOTCHA: Do not mix the two styles. If a handler throws for a validation
// problem, the pipeline below never sees Failure and the endpoint will
// return 500 instead of 400. Guard against that in integration tests.
// INTERVIEW: "Result<T> vs Either<L,R>?" → Result is a specialisation of
// Either where the Left side is always the Error type; it reads better in
// a line-of-business app than a generic Either.
// → Roadmap: Ay 1 · Faz 1 — Clean Architecture + Proje 1 İskeleti
public enum ErrorKind
{
    None,
    Validation,
    NotFound,
    Conflict,
    // Phase 10 (sync): a device token that is missing, wrong or revoked.
    Unauthorized,
    // Phase 10 (sync): client and server speak different protocol/schema versions.
    UpgradeRequired,
    // Phase 10 (media): a file or chunk over the configured size.
    TooLarge,
    // Phase 11 (GitHub): an outside service failed or could not be reached.
    Upstream,
    // Phase 14 (journal PIN): the resource exists but is locked (HTTP 423).
    Locked,
}

// WHY: The error says WHAT went wrong (kind + code + message), never which
// HTTP status to use. Handlers stay transport-free; ResultHttp maps kinds to
// status codes in one place.
// `Extensions` carries extra machine-readable fields (e.g. the id of the
// session that blocks a new one) — they end up as top-level ProblemDetails
// members.
public sealed record Error(ErrorKind Kind, string Code, string Message, IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static readonly Error None = new(ErrorKind.None, string.Empty, string.Empty);
    public static Error NotFound(string message) => new(ErrorKind.NotFound, "not_found", message);
    public static Error Validation(string message) => new(ErrorKind.Validation, "validation", message);
    public static Error Conflict(string message, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(ErrorKind.Conflict, "conflict", message, extensions);
}

public readonly record struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error Error { get; }

    private Result(bool success, T? value, Error error)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, Error.None);
    public static Result<T> Failure(Error error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}

// Unit — placeholder for commands that return nothing meaningful.
// WHY: avoids a parallel Result (non-generic) type; keeps one pipeline.
public readonly record struct Unit
{
    public static readonly Unit Value = new();
}
