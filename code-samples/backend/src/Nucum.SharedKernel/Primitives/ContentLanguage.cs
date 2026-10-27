// File summary: Which language the app's own shipped content is served in
// for the current request (Phase 19, DECISIONS F169, ADR 0047). The server
// stays language-free (errors are codes, the client translates); only
// content that ships WITH the app — the template gallery, plan module type
// notes, the Journal export — has an English twin.
// ContentLanguageMiddleware (Nucum.Api) reads the client's Accept-Language
// and opens a scope with Use(); modules read ContentLanguage.Current.
// Lives in Nucum.SharedKernel.Primitives.
namespace Nucum.SharedKernel.Primitives;

using System.Globalization;

public static class ContentLanguage
{
    public const string Turkish = "tr";
    public const string English = "en";

    public static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    public static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-GB");

    // CONCEPT: AsyncLocal<T> as per-request state.
    // An AsyncLocal value flows with the async call chain: set at the start of
    // a request, every handler awaited inside that request sees it, and no
    // other request does. HttpContext would do the same, but modules must not
    // depend on ASP.NET Core types.
    // WHY not CultureInfo.CurrentUICulture: outside a request (workers, unit
    // tests) it is the machine's Windows language. An English Windows would
    // then export the Journal in English from a background job, and a test
    // that expects Turkish would pass on one machine and fail on another.
    // Here the default is always Turkish, the original.
    // GOTCHA: a value set inside an awaited method does not flow back to the
    // caller; set it in the middleware, before `await next`.
    // INTERVIEW: "How do you keep per-request data without passing it
    // everywhere?" → AsyncLocal (what IHttpContextAccessor uses inside); keep
    // it small and always reset it when the scope ends.
    // → Roadmap: Ay 2 · Faz 7 — Localization & Globalization
    private static readonly AsyncLocal<string?> current = new();

    public static string Current => current.Value ?? Turkish;

    public static bool IsEnglish => Current == English;

    // The culture for formatting shipped text (dates in the Journal export).
    public static CultureInfo Culture => IsEnglish ? EnglishCulture : TurkishCulture;

    // Opens a language scope; disposing it puts the previous value back.
    public static IDisposable Use(string language)
    {
        var previous = current.Value;
        current.Value = language == English ? English : Turkish;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => current.Value = previous;
    }

    // Accept-Language may list several languages with weights
    // ("en-GB,en;q=0.9,tr;q=0.8"). The best-weighted one that we support
    // wins; nothing supported (or no header) means Turkish, the original.
    public static string FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return Turkish;
        var best = header.Split(',')
            .Select((part, index) =>
            {
                var bits = part.Split(';');
                var tag = bits[0].Trim().ToLowerInvariant();
                var q = 1.0;
                foreach (var b in bits.Skip(1))
                {
                    var kv = b.Trim();
                    if (kv.StartsWith("q=", StringComparison.Ordinal) &&
                        double.TryParse(kv[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) q = parsed;
                }
                return (Tag: tag, Q: q, Index: index);
            })
            .Where(x => x.Q > 0 && (IsTag(x.Tag, English) || IsTag(x.Tag, Turkish)))
            .OrderByDescending(x => x.Q).ThenBy(x => x.Index)
            .Select(x => IsTag(x.Tag, English) ? English : Turkish)
            .FirstOrDefault();
        return best ?? Turkish;
    }

    private static bool IsTag(string tag, string lang) =>
        tag == lang || tag.StartsWith(lang + "-", StringComparison.Ordinal);
}
