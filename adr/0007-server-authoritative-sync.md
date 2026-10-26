# ADR 0007 — Server-authoritative sync, idempotent command queue

Date: 2026-10-02 · Status: **Superseded by ADR 0029** (Phase 10, DECISIONS M4)

## Context
Nücum runs on the desktop (full data) and the phone (same React app
inside a PWA shell). The phone must work offline — the user marks a
routine on the train, then comes home. The desktop must be the single
source of truth; the phone must never diverge silently.

## Decision
The desktop API is **authoritative**. The phone:
1. **Reads** via REST + SignalR for live updates.
2. **Writes** by posting *commands* (not state) to a command queue
   endpoint, each tagged with a client-generated `commandId` (UUIDv7).
3. **Buffers** commands offline in IndexedDB; replays when the API is
   reachable again. The server deduplicates by `commandId` and applies
   each command idempotently.

## Alternatives
- **CRDT replication:** correct, but expensive to implement for every
  entity and alien to the code the user is studying.
- **Last-writer-wins over the whole row:** easy, but overwrites phone-
  only edits silently.
- **Peer-to-peer:** no central source of truth; harder to reason about
  streaks and achievements.

## Consequences
- (+) Phone can go offline and come back without losing or duplicating
  anything.
- (+) The command queue is a well-understood pattern; small code.
- (−) Phone adds UI for "queued" state (never acknowledged, waiting,
  retrying).
- (−) Idempotency must be enforced per command type; we add a check-the-
  command-id test for every command handler.
