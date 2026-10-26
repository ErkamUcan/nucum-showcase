# ADR 0006 — Logical day with configurable cutover

Date: 2026-10-02 · Status: Accepted

## Context
Users mark routines at 01:30 after a long night and expect that to count
as "yesterday", not "today". Streaks, stats, "today's" lists, and the
phone's data boundary all depend on one answer: when does a day begin?

## Decision
Introduce `ILogicalDateService` with a cutover hour (default 04:00) and a
timezone (default `Europe/Istanbul`). All "today" reasoning uses this
service — never `DateTime.Today`, never `DateOnly.FromDateTime` of local.

Storage: timestamps are **UTC** (`DateTimeOffset`), the logical day is a
`DateOnly` computed on the fly and persisted alongside events that are
day-scoped. The cutover hour is a user setting bound via `IOptions<T>`.

## Alternatives
- **Hard-coded midnight:** ignores the "I'm still awake" reality.
- **User-chosen rolling 24h window:** too flexible; wrecks compatibility
  between devices and makes streaks hard to explain.
- **Server-time only (UTC day):** wrong for anybody not on UTC.

## Consequences
- (+) One service, one answer; stats, streaks and today's list cannot
  disagree.
- (+) Easy to test with a fixed `TimeProvider`.
- (−) DST boundaries near 03:00 need care (the logical day computed
  "around" 03:00 shifts twice or zero times on the transition). We clamp
  with `TimeZoneInfo`'s native API and add tests for both DST edges.
- (−) The cutover is a setting; migration from "midnight" (old data)
  requires a one-time recomputation, documented in Phase 5's importer.
