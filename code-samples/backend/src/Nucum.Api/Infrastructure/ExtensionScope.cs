// File summary: Which requests the browser extension's own token may make
// (DECISIONS F126). The extension runs the desktop's Pomodoro page and focus
// mode, so it reads what those screens read and writes only what they write:
// focus sessions, the two activity-type helpers, a quick thought, ticking a
// card done on Bugün and its own "focus." / "ui." settings. Everything else
// (plans editing, backups, sync, GitHub, journal, export…) stays closed.
// Used by SessionTokenMiddleware. Lives in Nucum.Api.Infrastructure.
namespace Nucum.Api.Infrastructure;

using Microsoft.AspNetCore.Http;

public static class ExtensionScope
{
    // CONCEPT: Least privilege by route allow-list.
    // A second credential gets the smallest set of doors that its screens
    // need, listed in one place, so a review sees the whole surface at once.
    // WHY prefixes + exact patterns instead of endpoint metadata: the screens
    // reuse endpoints of several modules; tagging each module's routes would
    // spread the decision over many files.
    // INTERVIEW: "How do you scope a token without a full OAuth server?" →
    // a fixed allow-list of method + path per token kind, checked in one
    // middleware, tested both ways (allowed and refused).
    private static readonly string[] ReadPrefixes =
        ["/api/focus/", "/api/routines/", "/api/settings", "/api/today/", "/api/plans/", "/api/extension"];

    public static bool Allows(string method, PathString path)
    {
        var p = path.Value ?? "";
        if (HttpMethods.IsGet(method)) return ReadPrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.Ordinal));
        if (HttpMethods.IsPost(method))
        {
            return p == "/api/focus/sessions"
                || (p.StartsWith("/api/focus/sessions/", StringComparison.Ordinal) && !p.EndsWith("/minutes", StringComparison.Ordinal))
                || p == "/api/focus/types/free"
                || p.StartsWith("/api/focus/types/for-routine/", StringComparison.Ordinal)
                || p == "/api/thoughts/" || p == "/api/thoughts";
        }
        if (HttpMethods.IsPut(method))
        {
            // Only the extension's own look/feel settings (glass bar place,
            // focus theme, ambient sound, last custom pomodoro length).
            if (p.StartsWith("/api/settings/", StringComparison.Ordinal))
            {
                var key = Uri.UnescapeDataString(p["/api/settings/".Length..]);
                return key.StartsWith("focus.", StringComparison.Ordinal) || key.StartsWith("ui.", StringComparison.Ordinal);
            }
            // "Bitti" on the focus card of a Bugün item.
            return p.StartsWith("/api/today/", StringComparison.Ordinal) && p.EndsWith("/done", StringComparison.Ordinal);
        }
        return false;
    }
}
