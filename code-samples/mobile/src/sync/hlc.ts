// File summary: Hybrid Logical Clock for the phone — a TypeScript copy of
// backend/src/Nucum.SharedKernel/Sync/Hlc.cs (ADR 0029). Every local write
// gets a timestamp from `clock.now()`; every timestamp that arrives from the
// desktop goes through `clock.observe()`. Used by the replica store
// (replica.ts) and the sync client (client.ts). Pure; hlc.test.ts checks it
// against the same rules and the same text form as the C# tests.
//
// See CONCEPT: Hybrid Logical Clock in Hlc.cs — the short version: wall time
// that never goes backwards, plus a counter for ties, plus the device id as
// the last tie-breaker. String order of the encoded form = clock order.
// GOTCHA: JavaScript numbers are exact up to 2^53; milliseconds since 1970
// fit easily (2^41 today), so plain `number` is safe for wallMs.

export interface Hlc { wall: number; counter: number; node: string }

// "000001759900000000-00000-<32 hex>" — 18 + 1 + 5 + 1 + 32 = 57 characters.
export const HLC_LENGTH = 57;
const HLC_RE = /^(\d{18})-(\d{5})-([0-9a-f]{32})$/;

/** A device id (UUID, any casing, with or without dashes) in the "N" form C# uses. */
export function nodeOf(deviceId: string): string {
  return deviceId.replace(/-/g, '').toLowerCase();
}

export function encodeHlc(h: Hlc): string {
  return `${String(h.wall).padStart(18, '0')}-${String(h.counter).padStart(5, '0')}-${h.node}`;
}

export function parseHlc(text: string): Hlc | null {
  const m = HLC_RE.exec(text);
  if (!m || !m[1] || !m[2] || !m[3]) return null;
  return { wall: Number(m[1]), counter: Number(m[2]), node: m[3] };
}

/** Encoded HLCs compare correctly as plain strings; this is the readable name for it. */
export function compareHlc(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

// The same two step functions as HybridLogicalClock.Send / Receive in C#.
export function sendStep(lastWall: number, lastCounter: number, physical: number): [number, number] {
  return physical > lastWall ? [physical, 0] : [lastWall, lastCounter + 1];
}

export function receiveStep(lastWall: number, lastCounter: number, remoteWall: number, remoteCounter: number, physical: number): [number, number] {
  const wall = Math.max(lastWall, remoteWall, physical);
  if (wall === lastWall && wall === remoteWall) return [wall, Math.max(lastCounter, remoteCounter) + 1];
  if (wall === lastWall) return [wall, lastCounter + 1];
  if (wall === remoteWall) return [wall, remoteCounter + 1];
  return [wall, 0];
}

// One clock per app. JavaScript runs one thing at a time on the JS thread,
// so unlike the C# clock this needs no lock.
export class HlcClock {
  private wall = 0;
  private counter = 0;

  constructor(private readonly node: string, private readonly physical: () => number = Date.now) {}

  now(): string {
    [this.wall, this.counter] = sendStep(this.wall, this.counter, this.physical());
    return encodeHlc({ wall: this.wall, counter: this.counter, node: this.node });
  }

  observe(remote: string): void {
    const r = parseHlc(remote);
    if (!r) return;
    [this.wall, this.counter] = receiveStep(this.wall, this.counter, r.wall, r.counter, this.physical());
  }

  /** Start-up: continue after the newest timestamp already stored. */
  seedAtLeast(last: string | null): void {
    const h = last ? parseHlc(last) : null;
    if (h && (h.wall > this.wall || (h.wall === this.wall && h.counter > this.counter))) {
      this.wall = h.wall;
      this.counter = h.counter;
    }
  }
}
