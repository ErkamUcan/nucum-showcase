# ADR 0005 — Domain events + append-only activity log + transactional outbox

Date: 2026-10-02 · Status: Accepted

## Context
Modules must stay loosely coupled. "Routine completed" should reach
Gamification (streaks, XP), Insights (stats), Automation (rules), and
the phone (Sync) without the Routines module knowing any of them exist.
Delivery must survive a mid-flight crash.

## Decision
Three layers:
1. **Domain events** inside an aggregate's transaction. Dispatched after
   `SaveChanges` succeeds, synchronously in-process.
2. **Append-only activity log** (owned by the `Activity` module). Every
   state-changing command writes an event here in the same transaction.
   Replays and audits read it.
3. **Transactional outbox** for **integration events** that cross module
   boundaries. Written in the same transaction as the data change; a
   background `OutboxProcessor` publishes to subscribers; subscribers
   are idempotent.

## Alternatives
- **Call peers directly after SaveChanges:** a crash between the write
  and the call drops the message silently.
- **External message broker:** overkill for a single-process app; the
  user doesn't want to run a broker on their laptop.
- **Only domain events (no outbox):** fine for in-module reactions, but
  cross-module reactions must survive a crash.

## Consequences
- (+) Delivery is at-least-once with idempotent consumers — the standard
  guarantee for correctness without exotic infrastructure.
- (+) The activity log is also the sync fuel for the phone; one source
  of truth for "what has happened".
- (−) Subscribers must truly be idempotent. We add an integration test
  that fires the same event twice.
- (−) The outbox worker is more code to maintain; we document the
  lifecycle (unprocessed → processing → processed / failed).
