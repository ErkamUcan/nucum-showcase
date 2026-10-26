// File summary: Talks to the desktop's sync API (ADR 0029/0030) — pairing,
// push (outbox → desktop), pull (desktop → replica) — and runs one complete
// "sync round". Pure apart from the injected `fetch`, so client.test.ts runs
// it against a fake server and sync-desktop.test.ts against the real
// ASP.NET Core API. Called by src/data/sync-runner.ts (timer, app
// foreground, after local writes) and by the pairing screen.
//
// CONCEPT: Idempotent retries. A phone loses the network in the middle of a
// request all the time. Every push batch carries an idempotency key and every
// change its own id; if the answer is lost, the next round sends the same
// changes again and the desktop answers "duplicate" instead of applying them
// twice. The pull cursor moves only after a page is stored, so a crash
// re-pulls that page — applying a change twice is harmless (same HLC).
// INTERVIEW: "How do you make a POST safe to retry?" → client-generated
// idempotency key, server stores the answer for the key; plus naturally
// idempotent apply (versioned writes) as a second line of defence.
// → Roadmap: Ay 2 · Faz 3 — Sistem Tasarımına İlk Adım
import type { Replica } from './replica';
import {
  versionHeader, VERSION_HEADER,
  type ChangeDto, type MediaUploadDto, type PairResponse, type PullResponse, type PushResponse, type StartMediaUploadRequest,
} from './protocol';

export type FetchFn = (url: string, init?: { method?: string; headers?: Record<string, string>; body?: string | Uint8Array<ArrayBuffer> }) => Promise<{
  ok: boolean;
  status: number;
  json(): Promise<unknown>;
  text(): Promise<string>;
}>;

export type SyncErrorKind = 'offline' | 'version' | 'revoked' | 'unpaired' | 'server';

export class SyncError extends Error {
  constructor(readonly kind: SyncErrorKind, message: string, readonly update?: 'phone' | 'desktop') {
    super(message);
  }
}

export interface Connection { url: string; token: string }

export interface RoundResult { pushed: number; rejected: number; pulled: number; serverHlc: string | null }

const META_CURSOR = 'sync.cursor';
const PUSH_BATCH = 200;
const PULL_PAGE = 500;

export class SyncClient {
  constructor(private readonly replica: Replica, private readonly fetchFn: FetchFn) {}

  // ---- pairing ----

  async pair(url: string, code: string, deviceName: string): Promise<PairResponse> {
    const res = await this.call(`${url}/api/sync/pair`, 'POST', null, {
      code: code.trim().toUpperCase(),
      deviceId: this.replica.deviceId,
      deviceName,
      platform: 'android',
    });
    const body = (await res.json()) as PairResponse;
    this.replica.clock.observe(body.serverHlc);
    // A new pairing starts reading the desktop's log from the beginning.
    this.replica.setMeta(META_CURSOR, '0');
    return body;
  }

  // ---- one round: push everything, then pull everything ----

  async round(conn: Connection): Promise<RoundResult> {
    const push = await this.pushAll(conn);
    const pulled = await this.pullAll(conn);
    return { ...push, pulled: pulled.count, serverHlc: pulled.serverHlc };
  }

  async pushAll(conn: Connection): Promise<{ pushed: number; rejected: number }> {
    let pushed = 0;
    let rejected = 0;
    for (;;) {
      const batch = this.replica.outbox(PUSH_BATCH);
      if (batch.length === 0) return { pushed, rejected };
      const res = await this.call(`${conn.url}/api/sync/push`, 'POST', conn.token, {
        idempotencyKey: batchKey(batch),
        changes: batch,
      });
      const body = (await res.json()) as PushResponse;
      this.replica.clock.observe(body.serverHlc);
      const done: string[] = [];
      for (const r of body.results) {
        const sent = batch.find((c) => c.changeId === r.changeId);
        if (!sent) continue;
        // WHY a rejected change also leaves the outbox: the desktop will
        // refuse it again on every retry (a desktop-only table, a broken
        // row). Keeping it would block the queue forever; the reason goes
        // to the log the sync screen shows.
        if (r.status === 'rejected') { rejected++; this.replica.logRejected(sent, r.reason ?? 'rejected'); }
        if (r.status === 'merged' && r.rowId && r.rowId.toUpperCase() !== sent.rowId.toUpperCase()) this.replica.rekey(sent.table, sent.rowId, r.rowId);
        done.push(r.changeId);
      }
      this.replica.ack(done);
      pushed += done.length;
      // Nothing acknowledged = the server answered for none of them; stop
      // instead of looping on the same batch.
      if (done.length === 0) return { pushed, rejected };
    }
  }

  async pullAll(conn: Connection): Promise<{ count: number; serverHlc: string | null }> {
    let count = 0;
    let serverHlc: string | null = null;
    for (;;) {
      const after = Number(this.replica.meta(META_CURSOR) ?? '0');
      const res = await this.call(`${conn.url}/api/sync/pull?after=${after}&limit=${PULL_PAGE}`, 'GET', conn.token);
      const page = (await res.json()) as PullResponse;
      this.replica.applyPulled(page.changes);
      this.replica.clock.observe(page.serverHlc);
      // GOTCHA: move the cursor only after the page is stored (see CONCEPT).
      this.replica.setMeta(META_CURSOR, String(page.nextCursor));
      count += page.changes.length;
      serverHlc = page.serverHlc;
      if (!page.hasMore) return { count, serverHlc };
    }
  }

  // ---- media (chunked, resumable) ----

  async uploadMedia(conn: Connection, start: StartMediaUploadRequest, readChunk: (offset: number, length: number) => Promise<Uint8Array<ArrayBuffer>>): Promise<MediaUploadDto> {
    const res = await this.call(`${conn.url}/api/sync/media`, 'POST', conn.token, start);
    let state = (await res.json()) as MediaUploadDto;
    // The desktop says where to continue (ReceivedBytes), so a cut
    // connection resumes from the last stored byte, not from zero.
    while (!state.completed) {
      const length = Math.min(state.chunkBytes, state.totalBytes - state.receivedBytes);
      const bytes = await readChunk(state.receivedBytes, length);
      const put = await this.call(`${conn.url}/api/sync/media/${state.uploadId}?offset=${state.receivedBytes}`, 'PUT', conn.token, undefined, bytes);
      state = (await put.json()) as MediaUploadDto;
    }
    return state;
  }

  // ---- HTTP ----

  private async call(url: string, method: string, token: string | null, body?: unknown, bytes?: Uint8Array<ArrayBuffer>) {
    const headers: Record<string, string> = { [VERSION_HEADER]: versionHeader() };
    if (token) headers.Authorization = `Bearer ${token}`;
    let payload: string | Uint8Array<ArrayBuffer> | undefined;
    if (bytes) { headers['Content-Type'] = 'application/octet-stream'; payload = bytes; }
    else if (body !== undefined) { headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body); }
    let res;
    try {
      res = await this.fetchFn(url, { method, headers, body: payload });
    } catch (e) {
      throw new SyncError('offline', e instanceof Error ? e.message : String(e));
    }
    if (res.ok) return res;
    const text = await res.text().catch(() => '');
    if (res.status === 426) {
      const update = /"update"\s*:\s*"desktop"/.test(text) ? 'desktop' : 'phone';
      throw new SyncError('version', text, update);
    }
    if (res.status === 401) throw new SyncError(token ? 'revoked' : 'unpaired', text);
    throw new SyncError('server', `${res.status} ${text}`);
  }
}

// WHY a key derived from the batch: if the app dies after sending and before
// the answer, the next round rebuilds the SAME batch (same first and last
// change) and gets the stored answer back. A random key would make the
// desktop treat it as new (still safe thanks to per-change ids, but slower).
function batchKey(batch: ChangeDto[]): string {
  const first = batch[0]?.changeId ?? '';
  const last = batch[batch.length - 1]?.changeId ?? '';
  // Mix the last id into the first one; keep the UUID shape the desktop expects.
  const hex = (first + last).replace(/-/g, '');
  let a = 0;
  for (let i = 0; i < hex.length; i++) a = (a * 31 + hex.charCodeAt(i)) >>> 0;
  const tail = (a.toString(16).padStart(8, '0') + String(batch.length).padStart(4, '0')).slice(0, 12);
  return `${first.slice(0, 24)}${tail}`;
}
