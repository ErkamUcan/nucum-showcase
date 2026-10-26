// File summary: The conflict rule of the sync protocol as a pure function
// (ADR 0029): for each field an incoming change wants to write, decide win /
// lose / duplicate against the field's current version, and report the
// pairs that were truly concurrent so they can be logged. No I/O, no EF —
// unit tested in SyncMergeTests. Lives in Nucum.Modules.Sync.Domain; called
// by SyncApplier.
namespace Nucum.Modules.Sync.Domain;

using Nucum.SharedKernel.Sync;

// The version a field has on this desktop: the newest log entry that
// changed it.
public sealed record FieldVersion(Hlc Hlc, Guid? DeviceId);

public enum FieldOutcome
{
    Win,
    Lose,
    Duplicate,
}

public sealed record FieldDecision(string Field, FieldOutcome Outcome, bool Concurrent, FieldVersion? Current);

// CONCEPT: Last-writer-wins register, per field.
// Each field is its own little register: the value with the largest HLC is
// the value. Two devices editing DIFFERENT fields of one row both keep their
// edit; only the same field competes.
// "Concurrent" is a separate question from "who wins": the device tells us
// (baseHlc) the newest version of the row it had seen when it edited. If the
// field's current version came from another device AND is newer than that,
// the device edited without seeing it — a real conflict worth showing the
// user, even though LWW already picked a winner.
// WHY not log every lost field: a phone that was offline for a week and
// pushes an old edit of a field the desktop changed later is a conflict only
// if the phone never saw the desktop's change; an old edit replayed after
// the phone already merged it is just stale.
// GOTCHA: Equal HLC (same wall, counter AND device) means "I already have
// this exact write" — a retry. It must be a no-op, not a win, or a retried
// push would log itself again.
// INTERVIEW: "LWW loses data — when is that acceptable?" → for single-user,
// low-contention fields where the latest intent is what matters; keep a
// conflict log so a lost edit is visible, and never use LWW for counters
// (use append-only events instead).
// → Roadmap: Ay 2 · Faz 3 — Sistem Tasarımına İlk Adım
public static class FieldMerge
{
    public static IReadOnlyList<FieldDecision> Decide(
        IEnumerable<string> changed,
        Hlc incoming,
        Hlc? baseHlc,
        IReadOnlyDictionary<string, FieldVersion> current)
    {
        var decisions = new List<FieldDecision>();
        foreach (var field in changed.Distinct(StringComparer.Ordinal))
        {
            if (!current.TryGetValue(field, out var cur))
            {
                decisions.Add(new FieldDecision(field, FieldOutcome.Win, false, null));
                continue;
            }
            var cmp = incoming.CompareTo(cur.Hlc);
            if (cmp == 0)
            {
                decisions.Add(new FieldDecision(field, FieldOutcome.Duplicate, false, cur));
                continue;
            }
            var otherDevice = cur.Hlc.Node != incoming.Node;
            var unseen = baseHlc is not { } b || cur.Hlc > b;
            decisions.Add(new FieldDecision(field, cmp > 0 ? FieldOutcome.Win : FieldOutcome.Lose, otherDevice && unseen, cur));
        }
        return decisions;
    }

    // Folds the log entries of one row into "current version per field".
    public static Dictionary<string, FieldVersion> CurrentVersions(IEnumerable<(Hlc Hlc, Guid? DeviceId, IReadOnlyList<string> Changed)> history)
    {
        var map = new Dictionary<string, FieldVersion>(StringComparer.Ordinal);
        foreach (var (hlc, device, changed) in history)
        {
            foreach (var f in changed)
            {
                if (!map.TryGetValue(f, out var have) || hlc > have.Hlc) map[f] = new FieldVersion(hlc, device);
            }
        }
        return map;
    }
}
