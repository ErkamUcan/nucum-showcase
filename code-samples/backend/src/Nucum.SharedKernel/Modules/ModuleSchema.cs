// File summary: Shared helper for a modular monolith sharing one SQLite
// database between multiple DbContexts. EF Core's EnsureCreatedAsync is a
// no-op once ANY context created the database file, which leaves later
// modules without their tables. Phase 3 adds Settings/Activity/Routines on
// top of Plans; each module calls EnsureTablesCreatedAsync to create its
// own tables idempotently. Lives in Nucum.SharedKernel.Modules.
namespace Nucum.SharedKernel.Modules;

using Microsoft.EntityFrameworkCore;

// CONCEPT: Per-module idempotent schema create.
// Instead of EnsureCreatedAsync, each DbContext generates its own CREATE
// script via EF Core's model, then I rewrite the DDL to use
// "CREATE TABLE IF NOT EXISTS" / "CREATE INDEX IF NOT EXISTS" so running
// it on a database that already has some of these tables is safe.
// WHY: Full EF Migrations would be cleaner but add ceremony we don't need
// until Phase 4; this keeps development fast while still being correct
// for a multi-context setup.
// GOTCHA: This only works for SQLite's "IF NOT EXISTS" syntax. If we ever
// swap to Postgres (ADR 3) we switch to real migrations at the same time.
// INTERVIEW: "Why not Database.EnsureCreated?" → it checks for file
// existence, not table-set existence; after module A creates the file,
// module B thinks the DB is ready and skips its own CREATE TABLE.
public static class ModuleSchema
{
    public static async Task EnsureTablesCreatedAsync(DbContext db, CancellationToken ct)
    {
        var statements = ScriptStatements(db);
        foreach (var stmt in statements.Where(s => s.StartsWith("CREATE TABLE ", StringComparison.Ordinal)))
        {
            await db.Database.ExecuteSqlRawAsync(EscapeBraces(stmt.Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.Ordinal)), ct);
        }
        await SyncIndexesAsync(db, statements, ct);
    }

    // CONCEPT: Index sync (desired state, like a tiny migration tool).
    // "CREATE INDEX IF NOT EXISTS" skips an index whose NAME exists, even
    // when its definition changed — e.g. a unique index that became partial
    // ("… WHERE DeletedAtUtc IS NULL") in Phase 6.5. So I compare each index
    // the model wants with the SQL SQLite stored, recreate the ones that
    // differ, and drop EF-named ("IX_…") indexes the model no longer has.
    // WHY: Indexes hold no data, so dropping and recreating one is always
    // safe; only the table rebuild below touches rows.
    // GOTCHA: Only indexes on THIS context's tables are touched — other
    // modules share the same file.
    private static async Task SyncIndexesAsync(DbContext db, IReadOnlyList<string> statements, CancellationToken ct)
    {
        var wanted = statements
            .Where(s => s.StartsWith("CREATE INDEX ", StringComparison.Ordinal) || s.StartsWith("CREATE UNIQUE INDEX ", StringComparison.Ordinal))
            .ToDictionary(IndexName, s => s);
        var tables = db.Model.GetEntityTypes().Select(t => t.GetTableName()).OfType<string>().Distinct().ToList();
        foreach (var table in tables)
        {
            var existing = await db.Database
                .SqlQuery<IndexRow>($"SELECT name AS Name, sql AS Sql FROM sqlite_master WHERE type = 'index' AND tbl_name = {table} AND sql IS NOT NULL")
                .ToListAsync(ct);
            foreach (var index in existing)
            {
                var keep = wanted.TryGetValue(index.Name, out var want) && Normalize(want) == Normalize(index.Sql!);
                if (keep || !index.Name.StartsWith("IX_", StringComparison.Ordinal)) continue;
#pragma warning disable EF1002 // index names come from sqlite_master / the EF model, not from users
                await db.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS \"{index.Name}\"", ct);
#pragma warning restore EF1002
            }
        }
        foreach (var stmt in wanted.Values)
        {
            var ifNotExists = stmt
                .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.Ordinal)
                .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync(EscapeBraces(ifNotExists), ct);
        }
    }

    private sealed record IndexRow(string Name, string? Sql);

    private static List<string> ScriptStatements(DbContext db) =>
        db.Database.GenerateCreateScript()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0 && !s.StartsWith("GO", StringComparison.OrdinalIgnoreCase))
            .ToList();

    // `CREATE UNIQUE INDEX "IX_a" ON "t" ("C") …` → IX_a
    private static string IndexName(string stmt)
    {
        var start = stmt.IndexOf('"', StringComparison.Ordinal) + 1;
        return stmt[start..stmt.IndexOf('"', start)];
    }

    private static string Normalize(string sql) =>
        string.Join(' ', sql.Replace("IF NOT EXISTS ", "", StringComparison.OrdinalIgnoreCase)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // CONCEPT: Additive column migration.
    // "CREATE TABLE IF NOT EXISTS" never changes a table that already exists,
    // so a new property on an old table needs its own step. I read the
    // table's columns (PRAGMA table_info) and ALTER TABLE … ADD COLUMN only
    // the missing ones. Running it twice is a no-op.
    // `SqlType` is the whole column definition after the name, e.g. "TEXT"
    // (nullable) or "INTEGER NOT NULL DEFAULT 0" (a NOT NULL column needs a
    // default so existing rows get a value).
    // WHY: Only additive columns go through here — the cheap, safe
    // kind of migration. Renames or type changes would need a real migration
    // tool (and a backup first).
    // GOTCHA: Table/column names cannot be SQL parameters, so the caller must
    // pass trusted constants, never user input.
    // INTERVIEW: "How do you evolve a schema without downtime?" → expand first
    // (add nullable columns, backfill), contract later (drop old ones).
    public static async Task EnsureColumnsAsync(DbContext db, string table, IReadOnlyList<(string Name, string SqlType)> columns, CancellationToken ct)
    {
        // pragma_table_info takes the table name as a real parameter.
        var existing = await db.Database
            .SqlQuery<string>($"SELECT name AS Value FROM pragma_table_info({table})")
            .ToListAsync(ct);
        // No columns = the table does not exist yet; CREATE TABLE will make it
        // with every column, so callers may run this before or after create.
        if (existing.Count == 0) return;
        foreach (var (name, type) in columns)
        {
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
#pragma warning disable EF1002 // trusted constants only — see GOTCHA above
            await db.Database.ExecuteSqlRawAsync(EscapeBraces($"ALTER TABLE \"{table}\" ADD COLUMN \"{name}\" {type}"), ct);
#pragma warning restore EF1002
        }
    }

    // GOTCHA (Phase 13): ExecuteSqlRaw passes the text through string.Format
    // (for its {0} parameters), so a literal brace — a column default like
    // DEFAULT '{}' — must be doubled first.
    public static string EscapeBraces(string sql) =>
        sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

}
