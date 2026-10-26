// File summary: Hybrid Logical Clock (HLC) for the sync protocol (ADR 0029).
// `Hlc` is one timestamp (wall ms + counter + device id) with a fixed-width
// text form whose string order equals clock order; `HybridLogicalClock` is
// the per-process clock: Now() for a local write, Observe() when a remote
// timestamp arrives. Lives in Nucum.SharedKernel.Sync. Used by the change
// capture interceptor (SyncFields.cs), the Sync module and the fake mobile
// client in the tests.
namespace Nucum.SharedKernel.Sync;

using System.Globalization;

// CONCEPT: Hybrid Logical Clock (Kulkarni et al., 2014).
// Two devices that edit offline need to agree which edit is "later". Their
// wall clocks can be minutes apart (a phone with the wrong time), so plain
// UpdatedAt timestamps would let a slow clock always lose. A Lamport clock
// fixes the order but its numbers mean nothing to a human. An HLC is both:
// - wallMs: the largest physical time this clock has seen (local or remote),
// - counter: breaks ties when wallMs did not move,
// - node: the device id, the final tie-breaker so two clocks never compare equal.
// Rule: an event that happens after seeing another event gets a larger HLC,
// whatever the wall clocks say.
// WHY here: one type shared by capture, sync and tests; pure and unit tested.
// GOTCHA: Compare with CompareTo (or the encoded string), never by wallMs
// alone — two edits in the same millisecond differ only in counter/node.
// INTERVIEW: "Why not just use UTC timestamps for last-writer-wins?" → clock
// skew; a device whose clock is behind loses every edit. HLC keeps causality
// (happened-before) and stays close to real time.
// → Roadmap: Ay 2 · Faz 3 — Sistem Tasarımına İlk Adım
public readonly record struct Hlc(long WallMs, int Counter, Guid Node) : IComparable<Hlc>
{
    // "000001759900000000-00000-<32 hex>" — 18 + 1 + 5 + 1 + 32 characters.
    // Fixed widths make ordinal string order = clock order, so SQL ORDER BY
    // works on the stored text too.
    public const int EncodedLength = 57;

    public static Hlc Zero { get; } = new(0, 0, Guid.Empty);

    public int CompareTo(Hlc other)
    {
        var c = WallMs.CompareTo(other.WallMs);
        if (c != 0) return c;
        c = Counter.CompareTo(other.Counter);
        // Guid.CompareTo is not the same as comparing the hex text; use the
        // text so the in-memory order matches the stored order.
        return c != 0 ? c : string.CompareOrdinal(Node.ToString("N"), other.Node.ToString("N"));
    }

    public static bool operator <(Hlc a, Hlc b) => a.CompareTo(b) < 0;
    public static bool operator >(Hlc a, Hlc b) => a.CompareTo(b) > 0;
    public static bool operator <=(Hlc a, Hlc b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Hlc a, Hlc b) => a.CompareTo(b) >= 0;

    public string Encode() =>
        string.Create(CultureInfo.InvariantCulture, $"{WallMs:D18}-{Counter:D5}-{Node:N}");

    public override string ToString() => Encode();

    public static bool TryParse(string? text, out Hlc value)
    {
        value = Zero;
        if (text is null || text.Length != EncodedLength || text[18] != '-' || text[24] != '-') return false;
        if (!long.TryParse(text.AsSpan(0, 18), NumberStyles.None, CultureInfo.InvariantCulture, out var wall)) return false;
        if (!int.TryParse(text.AsSpan(19, 5), NumberStyles.None, CultureInfo.InvariantCulture, out var counter)) return false;
        if (!Guid.TryParseExact(text.AsSpan(25), "N", out var node)) return false;
        value = new Hlc(wall, counter, node);
        return true;
    }

    public static Hlc Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"Not an HLC: '{text}'.");

    // Baseline for rows written before Phase 10: their UpdatedAtUtc is the
    // best "when" we have, counter 0, the device that last wrote them.
    public static Hlc FromWallClock(DateTimeOffset at, Guid node) => new(at.ToUnixTimeMilliseconds(), 0, node);

    public DateTimeOffset WallTime => DateTimeOffset.FromUnixTimeMilliseconds(WallMs);
}

// The clock itself. One per process (singleton). Thread-safe with a lock:
// it is tiny and called once per SaveChanges, not per row.
public sealed class HybridLogicalClock(TimeProvider time)
{
    private readonly Lock _gate = new();
    private long _wall;
    private int _counter;

    // Pure step functions, separated so tests can check the rules without a
    // clock instance (HlcTests).
    public static (long Wall, int Counter) Send(long lastWall, int lastCounter, long physicalMs) =>
        physicalMs > lastWall ? (physicalMs, 0) : (lastWall, lastCounter + 1);

    public static (long Wall, int Counter) Receive(long lastWall, int lastCounter, long remoteWall, int remoteCounter, long physicalMs)
    {
        var wall = Math.Max(Math.Max(lastWall, remoteWall), physicalMs);
        int counter;
        if (wall == lastWall && wall == remoteWall) counter = Math.Max(lastCounter, remoteCounter) + 1;
        else if (wall == lastWall) counter = lastCounter + 1;
        else if (wall == remoteWall) counter = remoteCounter + 1;
        else counter = 0;
        return (wall, counter);
    }

    // A local event (a write on this device).
    public Hlc Now(Guid node)
    {
        lock (_gate)
        {
            (_wall, _counter) = Send(_wall, _counter, time.GetUtcNow().ToUnixTimeMilliseconds());
            return new Hlc(_wall, _counter, node);
        }
    }

    // A remote timestamp arrived (a pushed change). Moves this clock past it
    // so the next local write is ordered after what we just received.
    public void Observe(Hlc remote)
    {
        lock (_gate)
        {
            (_wall, _counter) = Receive(_wall, _counter, remote.WallMs, remote.Counter, time.GetUtcNow().ToUnixTimeMilliseconds());
        }
    }

    // Start-up: continue after the newest timestamp already in the log, so a
    // restart with a clock that went backwards still produces larger HLCs.
    public void SeedAtLeast(Hlc last)
    {
        lock (_gate)
        {
            if (last.WallMs > _wall || (last.WallMs == _wall && last.Counter > _counter))
            {
                _wall = last.WallMs;
                _counter = last.Counter;
            }
        }
    }

    public long PhysicalMs => time.GetUtcNow().ToUnixTimeMilliseconds();
}
