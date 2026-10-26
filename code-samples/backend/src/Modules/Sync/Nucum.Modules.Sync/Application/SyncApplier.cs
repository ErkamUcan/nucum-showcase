// File summary: The heart of push (ADR 0029). For each change a device sends:
// validate, skip if already seen (inbox), follow an id alias, move the HLC,
// then insert the row, merge it into a live row with the same natural key, or
// decide field by field with FieldMerge and write only the winners through
// the owning module's ISyncStore. Conflicts and the inbox go to the Sync
// module's own tables. Lives in Nucum.Modules.Sync.Application; called by
// PushChangesHandler.
namespace Nucum.Modules.Sync.Application;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nucum.Modules.Settings.Contracts;
using Nucum.Modules.Sync.Contracts;
using Nucum.Modules.Sync.Domain;
using Nucum.Modules.Sync.Infrastructure;
using Nucum.SharedKernel.Persistence;
using Nucum.SharedKernel.Primitives;
using Nucum.SharedKernel.Sync;

// Who is calling: the device row (pairing) and the phone's installation id.
public sealed record DeviceAuth(Guid PairingId, Guid DeviceId, string Name);

// CONCEPT: Idempotent receiver, two layers deep.
// A phone on a train sends a batch, the answer is lost, the phone sends it
// again. Layer 1: the batch's idempotency key — the same key returns the
// stored answer without touching anything. Layer 2: each change id is kept
// in an inbox — a change that comes back inside a NEW batch is "duplicate".
// And underneath both, the apply itself is idempotent: a change whose HLC
// equals the field's current version changes nothing (FieldMerge Duplicate).
// So even a crash between "row written" and "inbox written" cannot apply a
// change twice.
// WHY three layers: each catches a different failure — a lost response, a
// client that rebuilt its batch, a server crash mid-batch.
// GOTCHA: The stored batch answer must be returned AS IS. Re-running the
// batch would answer "duplicate" for every change, and a client that trusts
// the first answer it receives would think its edits were rejected.
// INTERVIEW: "How do you make a POST endpoint safe to retry?" → an
// Idempotency-Key header, store the response under it, return the stored
// response on a repeat; make the handler's effect idempotent too.
// → Roadmap: Ay 3 · Faz 2 — Idempotency Key
internal sealed class SyncApplier(
    SyncDbContext db,
    IEnumerable<ISyncStore> stores,
    IEnumerable<SyncDesktopOnlyTable> desktopOnly,
    IEnumerable<ISyncApplyHook> hooks,
    IEnumerable<ISyncBatchHook> batchHooks,
    HybridLogicalClock hlcClock,
    DeviceIdentity desktop,
    ISettingsService settings,
    TimeProvider clock,
    ILogger<SyncApplier> log)
{
    private readonly HashSet<string> _desktopOnly = desktopOnly.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
    private readonly List<ISyncApplyHook> _hooks = hooks.ToList();
    private bool _anyApplied;
    private const string KeyField = "Id";
    // Bookkeeping columns change with every write; a "conflict" on them says
    // nothing the user could act on, so they are never logged.
    private static readonly HashSet<string> Bookkeeping = [SyncColumns.UpdatedAtUtc, SyncColumns.DeviceId, "Version"];
    private readonly Dictionary<string, (ISyncStore Store, bool AppendOnly)> _tables = stores
        .SelectMany(s => s.Tables.Select(t => (t.Name, Store: s, t.AppendOnly)))
        .ToDictionary(x => x.Name, x => (x.Store, x.AppendOnly), StringComparer.Ordinal);

    public async Task<Result<PushResponse>> PushAsync(DeviceAuth device, PushRequest request, CancellationToken ct)
    {
        var pageSize = await settings.GetOrDefaultAsync(SettingsKeys.SyncPageSize, 500, ct);
        if (request.Changes.Count > pageSize)
            return new Error(ErrorKind.TooLarge, "sync.batch_too_large", $"At most {pageSize} changes per push.");

        var stored = await db.Batches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.DeviceId == device.PairingId && b.IdempotencyKey == request.IdempotencyKey, ct);
        if (stored is not null) return JsonSerializer.Deserialize<PushResponse>(stored.ResponseJson, SyncJson.Options)!;

        var maxDriftMs = (long)await settings.GetOrDefaultAsync(SettingsKeys.SyncMaxClockDriftMinutes, 1440, ct) * 60_000;
        var results = new List<ChangeResultDto>(request.Changes.Count);
        foreach (var change in request.Changes)
        {
            results.Add(await ApplyOneAsync(device, change, maxDriftMs, ct));
        }

        // F47: derived results (XP lines, achievements) are computed now, on
        // the desktop, so the phone's next pull already carries them.
        if (_anyApplied)
        {
            foreach (var hook in batchHooks) await hook.OnBatchAppliedAsync(ct);
        }

        var response = new PushResponse(results, hlcClock.Now(desktop.Current ?? Guid.Empty).Encode());
        db.Batches.Add(new BatchEntity
        {
            Id = UuidV7.NewId(clock),
            DeviceId = device.PairingId,
            IdempotencyKey = request.IdempotencyKey,
            ResponseJson = JsonSerializer.Serialize(response, SyncJson.Options),
            ReceivedAtUtc = clock.GetUtcNow(),
        });
        await TouchDeviceAsync(device, ct);
        await db.SaveChangesAsync(ct);
        return response;
    }

    private async Task<ChangeResultDto> ApplyOneAsync(DeviceAuth device, SyncChangeDto change, long maxDriftMs, CancellationToken ct)
    {
        ChangeResultDto Reject(string reason) => new(change.ChangeId, SyncProtocol.Rejected, change.RowId, reason);

        if (!_tables.TryGetValue(change.Table, out var target)) return Reject("unknown_table");
        // F47: rule results are written only by the desktop.
        if (_desktopOnly.Contains(change.Table)) return Reject("desktop_owned");
        if (string.IsNullOrWhiteSpace(change.RowId)) return Reject("bad_row_id");
        if (!Hlc.TryParse(change.Hlc, out var hlc)) return Reject("bad_hlc");
        // The HLC's node IS the writer: a device cannot write in another's name.
        if (hlc.Node != device.DeviceId) return Reject("hlc_device_mismatch");
        // CONCEPT: bounded clock drift — see Hlc.cs. One phone with its clock in
        // 2099 would otherwise push every HLC after it into 2099 too.
        if (hlc.WallMs > hlcClock.PhysicalMs + maxDriftMs) return Reject("clock_skew");
        Hlc? baseHlc = null;
        if (change.BaseHlc is not null)
        {
            if (!Hlc.TryParse(change.BaseHlc, out var b)) return Reject("bad_base_hlc");
            baseHlc = b;
        }
        var changed = change.Changed.Where(f => f != KeyField).Distinct(StringComparer.Ordinal).ToList();
        if (changed.Count == 0 || changed.Any(f => !change.Fields.ContainsKey(f))) return Reject("bad_changed");

        var seen = await db.Inbox.AsNoTracking().FirstOrDefaultAsync(i => i.ChangeId == change.ChangeId, ct);
        if (seen is not null) return new(change.ChangeId, SyncProtocol.Duplicate, seen.RowId, null);

        var rowId = await db.Aliases.AsNoTracking()
            .Where(a => a.TableName == change.Table && a.ForeignRowId == change.RowId)
            .Select(a => a.LocalRowId).FirstOrDefaultAsync(ct) ?? change.RowId;

        hlcClock.Observe(hlc);
        var (store, appendOnly) = target;
        string status;
        var merged = false;
        SyncRowSnapshot? before = null;
        try
        {
            var current = await store.ReadAsync(change.Table, rowId, ct);
            before = current;
            if (current is null && !appendOnly && IsLive(change.Fields)
                && await store.FindByNaturalKeyAsync(change.Table, change.Fields, ct) is { } localId)
            {
                // CONCEPT: natural-key merge (ADR 0029 "Natural keys"). The phone
                // made "the settings row for key X" offline while the desktop has
                // its own. Two live rows for one key would break the unique index,
                // so the phone's row folds into ours and the phone re-keys.
                db.Aliases.Add(new AliasEntity { Id = UuidV7.NewId(clock), TableName = change.Table, ForeignRowId = change.RowId, LocalRowId = localId, CreatedAtUtc = clock.GetUtcNow() });
                db.Conflicts.Add(new ConflictEntity
                {
                    Id = UuidV7.NewId(clock), DetectedAtUtc = clock.GetUtcNow(), Reason = "natural_key",
                    TableName = change.Table, RowId = localId, Field = "*",
                    WinnerDeviceId = desktop.Current, WinnerHlc = "", LoserDeviceId = device.DeviceId, LoserHlc = hlc.Encode(),
                    LoserValueJson = change.RowId,
                });
                rowId = localId;
                merged = true;
                current = await store.ReadAsync(change.Table, rowId, ct);
                before = current;
            }

            if (current is null)
            {
                await store.WriteAsync(new SyncWrite(change.Table, rowId, Insert: true, change.Fields,
                    new SyncApplyMeta(change.ChangeId, hlc, device.DeviceId, change.Fields.Keys.ToList())), ct);
                status = SyncProtocol.Applied;
            }
            else if (appendOnly)
            {
                status = SyncProtocol.Duplicate; // an event is inserted once and never changes
            }
            else
            {
                // After a natural-key merge the one "natural_key" entry says it
                // all; per-field entries would only repeat it.
                status = await MergeFieldsAsync(store, change, rowId, hlc, baseHlc, changed, current, device, logConflicts: !merged, ct);
            }
        }
        catch (SyncWriteException ex)
        {
            db.ChangeTracker.Clear();
            return Reject($"write_failed: {ex.Message}");
        }
        if (merged) status = SyncProtocol.Merged;
        if (status is SyncProtocol.Applied or SyncProtocol.Merged)
        {
            _anyApplied = true;
            await RunHooksAsync(store, change, rowId, before, device, ct);
        }

        db.Inbox.Add(new InboxEntity { ChangeId = change.ChangeId, DeviceId = device.DeviceId, Status = status, RowId = rowId, ReceivedAtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return new(change.ChangeId, status, rowId, null);
    }

    private async Task<string> MergeFieldsAsync(
        ISyncStore store, SyncChangeDto change, string rowId, Hlc hlc, Hlc? baseHlc, List<string> changed,
        SyncRowSnapshot current, DeviceAuth device, bool logConflicts, CancellationToken ct)
    {
        var history = await db.Changes.AsNoTracking()
            .Where(c => c.TableName == change.Table && c.RowId == rowId)
            .Select(c => new { c.Hlc, c.DeviceId, c.ChangedJson })
            .ToListAsync(ct);
        var versions = FieldMerge.CurrentVersions(history.Select(h => (
            Hlc.Parse(h.Hlc), h.DeviceId,
            (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(h.ChangedJson, SyncJson.Options) ?? []))));

        var decisions = FieldMerge.Decide(changed, hlc, baseHlc, versions);
        using var currentFields = JsonDocument.Parse(current.FieldsJson);
        foreach (var d in decisions.Where(d => logConflicts && d.Concurrent && !Bookkeeping.Contains(d.Field)))
        {
            // Re-serialized so both sides use the same escaping (see SyncJson).
            var ours = currentFields.RootElement.TryGetProperty(d.Field, out var v) ? JsonSerializer.Serialize(v, SyncJson.Options) : null;
            var theirs = JsonSerializer.Serialize(change.Fields[d.Field], SyncJson.Options);
            var incomingWins = d.Outcome == FieldOutcome.Win;
            db.Conflicts.Add(new ConflictEntity
            {
                Id = UuidV7.NewId(clock),
                DetectedAtUtc = clock.GetUtcNow(),
                Reason = "concurrent_edit",
                TableName = change.Table,
                RowId = rowId,
                Field = d.Field,
                WinnerDeviceId = incomingWins ? device.DeviceId : d.Current!.DeviceId,
                WinnerHlc = incomingWins ? hlc.Encode() : d.Current!.Hlc.Encode(),
                WinnerValueJson = incomingWins ? theirs : ours,
                LoserDeviceId = incomingWins ? d.Current!.DeviceId : device.DeviceId,
                LoserHlc = incomingWins ? d.Current!.Hlc.Encode() : hlc.Encode(),
                LoserValueJson = incomingWins ? ours : theirs,
            });
        }

        var winners = decisions.Where(d => d.Outcome == FieldOutcome.Win).Select(d => d.Field).ToList();
        if (winners.Count > 0)
        {
            await store.WriteAsync(new SyncWrite(change.Table, rowId, Insert: false, change.Fields,
                new SyncApplyMeta(change.ChangeId, hlc, device.DeviceId, winners)), ct);
            return SyncProtocol.Applied;
        }
        return decisions.All(d => d.Outcome == FieldOutcome.Duplicate) ? SyncProtocol.Duplicate : SyncProtocol.Stale;
    }

    public async Task<Result<Unit>> UseLoserValueAsync(Guid conflictId, CancellationToken ct)
    {
        var conflict = await db.Conflicts.FirstOrDefaultAsync(c => c.Id == conflictId, ct);
        if (conflict is null) return Error.NotFound("No such conflict.");
        if (conflict.Reason != "concurrent_edit" || conflict.LoserValueJson is null)
            return new Error(ErrorKind.Validation, "sync.conflict_not_restorable", "Only a field conflict has a value to restore.");
        if (!_tables.TryGetValue(conflict.TableName, out var target)) return Error.NotFound("The table no longer syncs.");

        var before = await target.Store.ReadAsync(conflict.TableName, conflict.RowId, ct);
        if (before is null) return Error.NotFound("The row is gone.");
        using var value = JsonDocument.Parse(conflict.LoserValueJson);
        await target.Store.WriteLocalFieldAsync(conflict.TableName, conflict.RowId, conflict.Field, value.RootElement, ct);
        conflict.ResolvedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        // Same rule path as a pushed change: a restored "Done" counts once.
        var change = new SyncChangeDto(UuidV7.NewId(clock), conflict.TableName, conflict.RowId, hlcClock.Now(desktop.Current ?? Guid.Empty).Encode(),
            new Dictionary<string, JsonElement>(), [conflict.Field], null);
        await RunHooksAsync(target.Store, change, conflict.RowId, before, new DeviceAuth(Guid.Empty, desktop.Current ?? Guid.Empty, ""), ct);
        foreach (var hook in batchHooks) await hook.OnBatchAppliedAsync(ct);
        return Unit.Value;
    }

    // F47: let the owning module turn the row's transition into its domain event.
    private async Task RunHooksAsync(ISyncStore store, SyncChangeDto change, string rowId, SyncRowSnapshot? before, DeviceAuth device, CancellationToken ct)
    {
        var handlers = _hooks.Where(h => h.Handles(change.Table)).ToList();
        if (handlers.Count == 0) return;
        var after = await store.ReadAsync(change.Table, rowId, ct);
        if (after is null) return;
        var row = new SyncAppliedRow(change.Table, rowId, before is null ? null : Fields(before.FieldsJson), Fields(after.FieldsJson), change.ChangeId, device.DeviceId);
        foreach (var hook in handlers)
        {
            try
            {
                await hook.OnAppliedAsync(row, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // GOTCHA: the row is already committed. A failing rule must not
                // turn the push into an error (the phone would retry a change
                // that is applied); it is logged, and the hook can run again
                // because it only reacts to transitions it has not handled.
                log.LogError(ex, "Sync hook {Hook} failed for {Table}/{Row}.", hook.GetType().Name, change.Table, rowId);
            }
        }
    }

    private static Dictionary<string, JsonElement> Fields(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SyncJson.Options)!;

    private static bool IsLive(IReadOnlyDictionary<string, JsonElement> fields) =>
        !fields.TryGetValue(SyncColumns.DeletedAtUtc, out var d) || d.ValueKind == JsonValueKind.Null;

    private async Task TouchDeviceAsync(DeviceAuth device, CancellationToken ct)
    {
        var row = await db.Devices.FirstAsync(d => d.Id == device.PairingId, ct);
        row.LastSeenAtUtc = row.LastSyncAtUtc = clock.GetUtcNow();
    }
}
