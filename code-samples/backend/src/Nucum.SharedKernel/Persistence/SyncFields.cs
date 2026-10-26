// File summary: The sync contract every replicated row follows (ADR 0017,
// MOBILE.md §5): UpdatedAtUtc + DeviceId + DeletedAtUtc next to a UUIDv7 Id.
// ISyncTracked marks such rows, SyncStampInterceptor fills the three fields
// on every SaveChanges and turns deletes into soft deletes, and
// ApplySyncConventions adds the soft-delete query filter and partial unique
// indexes. DeviceIdentity holds this installation's id. Lives in
// Nucum.SharedKernel.Persistence; used by every module DbContext.
namespace Nucum.SharedKernel.Persistence;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Runtime.CompilerServices;
using Nucum.SharedKernel.Sync;

// CONCEPT: Sync-ready row (last-writer-wins metadata).
// When the phone and the desktop both change data offline, the merge needs
// three facts per row: WHEN it last changed (UpdatedAtUtc), WHO changed it
// (DeviceId), and whether it was DELETED (DeletedAtUtc — a "tombstone"). A
// hard DELETE leaves nothing to replicate: the other device would never
// learn the row is gone and would send it back.
// WHY: Faz 10 builds the sync endpoints; adding these columns later would
// mean another migration over real data. Today they cost three columns.
// GOTCHA: The Id must be created on the device (UUIDv7), never by the
// database, so two devices can create rows offline without clashing.
// INTERVIEW: "How do you delete in an offline-first system?" → soft delete
// with a tombstone timestamp; hard-delete (compact) only after every device
// has seen the tombstone.
// → Roadmap: Ay 1 · Faz 1 — EF Core Temelleri & Migration
public interface ISyncTracked
{
    DateTimeOffset UpdatedAtUtc { get; set; }
    Guid? DeviceId { get; set; }
    DateTimeOffset? DeletedAtUtc { get; set; }
}

public static class SyncColumns
{
    public const string UpdatedAtUtc = nameof(ISyncTracked.UpdatedAtUtc);
    public const string DeviceId = nameof(ISyncTracked.DeviceId);
    public const string DeletedAtUtc = nameof(ISyncTracked.DeletedAtUtc);

    // SQL filter for unique indexes: a tombstone must not block a new live
    // row with the same key (e.g. a progress row un-done, then done again).
    public const string NotDeletedFilter = "\"DeletedAtUtc\" IS NULL";
}

// This installation's id. Set once at start-up by the Settings module (it
// owns the device-local `settings_device` row); null only before that.
// WHY: a singleton value, not a DB lookup per save — the interceptor runs on
// every SaveChanges.
public sealed class DeviceIdentity
{
    public Guid? Current { get; private set; }

    public void Set(Guid id) => Current = id;
}

// CONCEPT: SaveChanges interceptor.
// EF Core calls this right before it writes. I walk the change tracker once
// and stamp every ISyncTracked row, so no handler can forget it.
// Deleted rows are switched to Modified with DeletedAtUtc set: the "delete"
// becomes an UPDATE, the row stays as a tombstone, and the global query
// filter hides it from every normal query.
// WHY: One rule in one place. Before Phase 6.5 each service set UpdatedAt by
// hand and half the tables had no DeletedAt at all.
// GOTCHA: ExecuteDelete/ExecuteUpdate and raw SQL bypass the change tracker,
// so they bypass this too. They are allowed only on non-synced tables (pack
// content, see ADR 0017).
// INTERVIEW: "Interceptor vs overriding SaveChanges?" → an interceptor is
// shared by every DbContext and is resolved from DI (clock, device id), so
// tests can swap the clock; an override must be copied into each context.
// → Roadmap: Ay 1 · Faz 2 — EF Core — Yapılandırma
// Phase 10 (ADR 0029): the same pass also writes the change log. After the
// stamp, SyncChangeCapture adds one `sync_changes` row per written row to
// this SaveChanges, so the row and its log entry commit together. Rows the
// Sync module writes for another device (SyncApplyScope) keep that device's
// id and HLC instead of this machine's.
public sealed class SyncStampInterceptor(
    TimeProvider clock,
    DeviceIdentity device,
    HybridLogicalClock hlc,
    IEnumerable<SyncAppendOnlyTable> appendOnlyTables,
    ISyncChangeNotifier notifier) : SaveChangesInterceptor
{
    private readonly HashSet<string> _appendOnly = appendOnlyTables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

    // Whether each in-flight SaveChanges added log rows (and whether any came
    // from another device), so SavedChanges knows whom to notify.
    // ConditionalWeakTable: no leak if a save throws.
    private readonly ConditionalWeakTable<DbContext, StrongBox<bool>> _pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Prepare(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Prepare(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Committed(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Committed(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Forget(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Prepare(DbContext? db)
    {
        if (db is null) return;
        var now = clock.GetUtcNow();
        Stamp(db, now);
        var (captured, remote) = SyncChangeCapture.Capture(db, hlc, device.Current, _appendOnly, now);
        if (captured > 0) _pending.AddOrUpdate(db, new StrongBox<bool>(remote));
    }

    private void Committed(DbContext? db)
    {
        if (db is null || !_pending.TryGetValue(db, out var remote)) return;
        _pending.Remove(db);
        // The log rows are written; nobody needs them tracked any more.
        foreach (var e in db.ChangeTracker.Entries<SyncChangeRecord>().ToList()) e.State = EntityState.Detached;
        notifier.ChangesCommitted(remote.Value);
    }

    private void Forget(DbContext? db)
    {
        if (db is null) return;
        _pending.Remove(db);
        // A failed save must not leave log rows behind for the next save.
        foreach (var e in db.ChangeTracker.Entries<SyncChangeRecord>().Where(e => e.State == EntityState.Added).ToList())
            e.State = EntityState.Detached;
    }

    private void Stamp(DbContext db, DateTimeOffset now)
    {
        foreach (var entry in db.ChangeTracker.Entries<ISyncTracked>())
        {
            // A remote write (Sync module): UpdatedAtUtc and DeletedAtUtc
            // come from the other device's row; only the device id is set.
            if (SyncApplyScope.Find(entry.Entity) is { } remote)
            {
                entry.Entity.DeviceId = remote.DeviceId;
                continue;
            }
            switch (entry.State)
            {
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.DeletedAtUtc = now;
                    goto case EntityState.Modified;
                case EntityState.Added:
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.DeviceId = device.Current;
                    break;
            }
        }
    }
}

public static class SyncModelConventions
{
    // Call at the END of OnModelCreating (after every HasIndex), so the
    // unique indexes it rewrites already exist.
    public static ModelBuilder ApplySyncConventions(this ModelBuilder modelBuilder)
    {
        // Phase 10: every context with synced rows also maps the change log,
        // so the interceptor can write it in the same transaction (ADR 0029).
        modelBuilder.MapSyncChangeLog();
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(t => typeof(ISyncTracked).IsAssignableFrom(t.ClrType)))
        {
            // CONCEPT: Global query filter.
            // EF adds `WHERE DeletedAtUtc IS NULL` to every query on this
            // type (Include and navigation loads too). Code that must see
            // tombstones says so with IgnoreQueryFilters().
            // GOTCHA: Built as an expression tree per CLR type, because the
            // generic HasQueryFilter<T> needs T at compile time.
            var p = Expression.Parameter(entity.ClrType, "e");
            var deleted = Expression.Property(p, nameof(ISyncTracked.DeletedAtUtc));
            var filter = Expression.Lambda(Expression.Equal(deleted, Expression.Constant(null, typeof(DateTimeOffset?))), p);
            modelBuilder.Entity(entity.ClrType).HasQueryFilter(filter);

            foreach (var index in entity.GetIndexes().Where(i => i.IsUnique && i.GetFilter() is null))
            {
                ((IMutableIndex)index).SetFilter(SyncColumns.NotDeletedFilter);
            }
        }
        return modelBuilder;
    }
}
