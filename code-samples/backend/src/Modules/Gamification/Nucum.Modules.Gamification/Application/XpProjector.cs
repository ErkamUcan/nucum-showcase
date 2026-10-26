// File summary: The XP projection. Reads the activity log from its last
// checkpoint, runs each new event through XpRules (pure) and appends the
// resulting lines to gamification_xp_ledger. RebuildAsync drops the
// projection and replays the whole log — the result must be identical
// (XpProjectionTests proves it). DayEvaluator writes the
// "gamification.day_evaluated" events the streak bonus is paid from.
// Lives in Nucum.Modules.Gamification.Application.
namespace Nucum.Modules.Gamification.Application;

using Microsoft.EntityFrameworkCore;
using Nucum.Modules.Activity.Contracts;
using Nucum.Modules.Gamification.Contracts;
using Nucum.Modules.Gamification.Domain;
using Nucum.Modules.Gamification.Infrastructure;
using Nucum.Modules.Routines.Contracts;
using Nucum.Modules.Settings.Contracts;
using Nucum.SharedKernel.Persistence;
using Nucum.SharedKernel.Sync;

// CONCEPT: Projection (read model) built from an event log.
// The activity log says what happened; the XP ledger is one way of LOOKING
// at it. A projection is a function from the log to a table:
//     ledger = fold(XpRules.Apply, empty, log)
// Because it is a function, it can be thrown away and computed again at any
// time ("Yeniden hesapla" in Ayarlar). Two properties make that safe:
//   1. Deterministic: same events in the same order → the same lines with
//      the same ids (XpRules.LineId).
//   2. Idempotent: an event already handled is remembered in
//      gamification_xp_seen and never handled twice.
// WHY catch up on read instead of a background worker: the projection runs
// right before the summary is read, so the sidebar is never behind and no
// timer has to be tuned. Reading the log from a checkpoint is cheap.
// GOTCHA: events are read in (OccurredAtUtc, Id) order. Two requests a few
// milliseconds apart can commit out of that order; I re-read a small safety
// window before the checkpoint and skip ids already seen, so a late commit
// is still picked up (its lines may then sit in a slightly different order
// than a rebuild would produce — the totals are the same).
// INTERVIEW: "Projection vs storing the total?" → a stored total drifts with
// every bug; a projection can always be rebuilt from the source of truth.
// → Roadmap: Ay 2 · Faz 4 — Event Sourcing & Projeksiyonlar
internal sealed class XpProjector(
    GamificationDbContext db,
    IActivityReader activity,
    IRoutinesReader routines,
    ISettingsService settings,
    HybridLogicalClock hlc,
    DeviceIdentity device,
    TimeProvider clock)
{
    private static readonly TimeSpan SafetyWindow = TimeSpan.FromMinutes(10);
    private const string LedgerTable = "gamification_xp_ledger";

    // Returns how many new events were handled (0 = the ledger was current).
    public async Task<int> ProjectAsync(CancellationToken ct)
    {
        var checkpoint = await db.Seen.AsNoTracking()
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => (DateTimeOffset?)x.OccurredAtUtc)
            .FirstOrDefaultAsync(ct);
        var from = checkpoint is { } c ? c - SafetyWindow : DateTimeOffset.MinValue;
        var events = await activity.ListSinceAsync(from, ct);
        var seen = (await db.Seen.AsNoTracking().Where(x => x.OccurredAtUtc >= from).Select(x => x.EventId).ToListAsync(ct)).ToHashSet();
        var fresh = events.Where(e => !seen.Contains(e.Id)).ToList();
        if (fresh.Count == 0) return 0;

        var state = await LoadStateAsync(ct);
        var weights = await settings.GetOrDefaultAsync(SettingsKeys.XpWeights, XpDefaults.Weights, ct);
        var levels = (await routines.ListAsync(ct)).ToDictionary(r => r.Id.ToString("N"), r => r.Level.ToString());

        foreach (var e in fresh)
        {
            var lines = XpRules.Apply(
                new XpEvent(e.Id, e.OccurredAtUtc, e.LogicalDate, e.Type, e.SubjectId, e.PayloadJson),
                state, weights, id => levels.GetValueOrDefault(id));
            foreach (var l in lines)
            {
                db.Ledger.Add(new XpLedgerLineEntity
                {
                    Id = l.Id,
                    SourceEventId = l.SourceEventId,
                    LineNo = l.LineNo,
                    LogicalDate = l.LogicalDate,
                    SubjectKey = l.SubjectKey,
                    Kind = l.Kind,
                    Amount = l.Amount,
                    Reason = l.Reason,
                    OccurredAtUtc = l.OccurredAtUtc,
                });
            }
            db.Seen.Add(new XpSeenEntity { EventId = e.Id, OccurredAtUtc = e.OccurredAtUtc });
        }
        await db.SaveChangesAsync(ct);
        return fresh.Count;
    }

    // "Yeniden hesapla": delete the projection, replay the whole log.
    // WHY ExecuteDelete is allowed here: both tables are projections, not
    // user data (ADR 0017 exception list) — nothing is lost that the log
    // cannot give back.
    public async Task<XpRebuildResultDto> RebuildAsync(CancellationToken ct)
    {
        var before = await TotalAsync(ct);
        var oldIds = await db.Ledger.AsNoTracking().Select(x => x.Id).ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Ledger.ExecuteDeleteAsync(ct);
        await db.Seen.ExecuteDeleteAsync(ct);
        db.ChangeTracker.Clear();
        await ProjectAsync(ct);
        // Phase 12 (ADR 0035): ExecuteDelete is invisible to the change log, so
        // a line the replay no longer produces would live on in a phone. Lines
        // that came back have the same id (XpRules.LineId) and travel as new
        // inserts; the ones that did not come back get a purge record.
        // See CONCEPT "Purge record" in SharedKernel/Sync/ChangeLog.cs.
        var newIds = (await db.Ledger.AsNoTracking().Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        var removed = oldIds.Where(id => !newIds.Contains(id)).Select(id => SyncKeys.ToText(id)).ToList();
        if (removed.Count > 0)
        {
            SyncPurge.Add(db, LedgerTable, removed, hlc, device.Current, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new XpRebuildResultDto(before, await TotalAsync(ct), await db.Ledger.CountAsync(ct));
    }

    public async Task<long> TotalAsync(CancellationToken ct) =>
        await db.Ledger.AsNoTracking().SumAsync(x => (long)x.Amount, ct);

    private async Task<XpLedgerState> LoadStateAsync(CancellationToken ct)
    {
        var state = new XpLedgerState();
        var lines = await db.Ledger.AsNoTracking().OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.SourceEventId).ThenBy(x => x.LineNo).ToListAsync(ct);
        foreach (var l in lines)
            state.Add(new XpLine(l.Id, l.SourceEventId, l.LineNo, l.LogicalDate, l.SubjectKey, l.Kind, l.Amount, l.Reason, l.OccurredAtUtc));
        return state;
    }
}

// CONCEPT: Turning a computed fact into an event.
// The streak bonus depends on the streak, and the streak is computed from
// routine entries — not from the log. To keep "XP comes only from events",
// the evaluator writes the verdict for each finished day into the log
// ("2026-10-06: kept, 10 % bonus"). The projection pays from that event.
// When a past day is filled in later and its verdict changes, a new event
// with the new verdict is written; XpRules moves the bonus to match.
// WHY only finished days: today can still change; its verdict is written
// tomorrow (the first summary read after the day turns at 04:00).
// GOTCHA: the bonus is a percentage of the day's XP at the moment the
// verdict is processed. Filling in more of an already-judged day adds the
// base XP but does not re-price the bonus unless the verdict itself changes.
internal sealed class DayEvaluator(IActivityReader activity, IActivityWriter writer)
{
    public async Task<int> EvaluateAsync(StreakWorld w, CancellationToken ct)
    {
        var main = StreakService.Main(w);
        var last = new Dictionary<DateOnly, (bool Kept, int Percent)>();
        foreach (var e in await activity.ListByTypesAsync([GamificationEventTypes.DayEvaluated], ct))
        {
            if (!DateOnly.TryParse(e.SubjectId, System.Globalization.CultureInfo.InvariantCulture, out var d)) continue;
            using var doc = System.Text.Json.JsonDocument.Parse(e.PayloadJson);
            var root = doc.RootElement;
            last[d] = (root.TryGetProperty("kept", out var k) && k.GetBoolean(), root.TryGetProperty("percent", out var p) ? p.GetInt32() : 0);
        }

        var written = 0;
        foreach (var day in main.Days.Where(d => d.Date < w.Today))
        {
            var kept = day.Status == StreakDayStatus.Kept;
            var percent = kept ? XpRules.BonusPercent(day.Length, w.Settings.StreakBonuses) : 0;
            if (last.TryGetValue(day.Date, out var prev))
            {
                if (prev == (kept, percent)) continue;
            }
            else if (percent == 0)
            {
                continue; // nothing to pay and nothing to take back
            }
            var iso = StreakService.Iso(day.Date);
            await writer.AppendAsync(new ActivityAppend("gamification", GamificationEventTypes.DayEvaluated, iso,
                new { date = iso, kept, percent, length = day.Length }), ct);
            written++;
        }
        return written;
    }
}
