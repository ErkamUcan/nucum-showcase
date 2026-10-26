# ADR 0033 — Global search: SQLite FTS5, Turkish folding, rebuilt from the change log

**Status:** Accepted · 2026-10-09 · Phase 11
**Related:** ADR 0003 (SQLite), ADR 0029 (`sync_changes`), SPEC §20

## Context

SPEC §20 wants one search over plan content, notebooks, notes and routines,
"Türkçe karakter duyarsız (ı/i, ş/s…)", opening and highlighting the result.
The text lives in four modules, partly as HTML (plan blocks) and TipTap JSON
(notes). The data set is personal: thousands of rows, not millions.

## Decision

- **SQLite FTS5** virtual table `search_index` in the same database file.
  Display columns (title, body, link, kind, context) are `UNINDEXED`; two
  indexed columns hold the **folded** title and body. `bm25` ranks title
  hits ×10. Queries are prefix terms joined with AND (`"isci"* "is"*`).
- **Folding by hand, one character to one character**: lower case,
  ı/İ/I→i, ş→s, ğ→g, ü→u, ö→o, ç→c, other accents stripped. The same
  `SearchText.Fold` runs on documents and on the query. Because the length
  never changes, a match position in folded text is the highlight position in
  the original — excerpts and highlights are computed in C# from the original.
- **Sources, not tables**: each owning module registers an `ISearchSource`
  (SharedKernel) that turns its rows into `SearchDocument`s with an in-app
  link (`/planlar/<slug>?dugum=<node>`, `?defter=<slug>`, `/rutinler?ara=`).
  The Search module references no other module.
- **Freshness from the change log**: before a query, compare
  `MAX(sync_changes.Seq)` (+ an index version) with the value stored in
  `search_state`; if it moved, rebuild the whole index in one transaction.

## Alternatives considered

- **`LIKE '%…%'`**: no index use, no ranking, no word boundaries, and
  SQLite's `LIKE` folds only ASCII case.
- **Triggers per table / incremental updates**: four modules, HTML and JSON
  sources; every new searchable table would need trigger code. The change
  log already says "something changed".
- **ICU tokenizer / Turkish collation in SQLite**: not in the bundled
  `e_sqlite3`; would need a native build.
- **External engine (Lucene.NET, Meilisearch)**: a second store to back up and
  keep in sync; overkill for a personal data set.

## Consequences

- The first search after a write pays for a full rebuild (milliseconds to a
  few hundred ms at personal scale); later searches are index lookups.
- The index is derived: not replicated, not exported, cleared on start-up and
  after a restore; backups skip FTS shadow tables when counting rows.
- If data grows a lot, the rebuild can become incremental by reading only the
  `sync_changes` rows after the stored cursor — the seam is already there.
