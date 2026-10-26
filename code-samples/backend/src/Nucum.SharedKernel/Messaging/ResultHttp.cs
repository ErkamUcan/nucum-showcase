// File summary: The ONE place that turns a handler's Result<T> into an HTTP
// response. Success → the shape the endpoint asks for (200 + body, 204, raw
// JSON text); failure → RFC 9457 ProblemDetails with the status picked by
// ErrorKind. Lives in SharedKernel.Messaging; used by every module's
// Endpoints/ class.
namespace Nucum.SharedKernel.Messaging;

using Microsoft.AspNetCore.Http;
using Nucum.SharedKernel.Primitives;

// CONCEPT: ProblemDetails (RFC 9457).
// A standard JSON error body: { type, title, status, detail, …extensions }.
// Clients (our React app, later the phone) read one shape for every error
// instead of guessing between { error }, { message } and an empty body —
// which is exactly what the endpoints returned before Phase 6.5.
// WHY: Handlers know nothing about HTTP. If the same command is later sent
// by the sync layer (Faz 10), it gets the same Result and maps it its own
// way. The status table below is the only HTTP knowledge in the system.
// INTERVIEW: "Where do you map domain errors to status codes?" → at the edge,
// in one function; never `return StatusCode(404)` from inside business code.
// → Roadmap: Ay 2 · Faz 5 — OpenAPI & API Dokümantasyonu
public static class ResultHttp
{
    public static IResult ToProblem(Error error)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.UpgradeRequired => StatusCodes.Status426UpgradeRequired,
            ErrorKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
            ErrorKind.Upstream => StatusCodes.Status502BadGateway,
            ErrorKind.Locked => StatusCodes.Status423Locked,
            _ => StatusCodes.Status500InternalServerError,
        };
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.Extensions is not null)
        {
            foreach (var (key, value) in error.Extensions) extensions[key] = value;
        }
        return Results.Problem(detail: error.Message, statusCode: status, extensions: extensions);
    }

    // 200 + JSON body.
    public static IResult ToOk<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);

    // 204 for commands that return nothing.
    public static IResult ToNoContent<T>(this Result<T> result) =>
        result.IsSuccess ? Results.NoContent() : ToProblem(result.Error);

    // Any other success shape (raw JSON text, 201…) — the endpoint decides.
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value!) : ToProblem(result.Error);
}
