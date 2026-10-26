// File summary: Minimal fetch wrapper that attaches the Nücum session token
// to every request. In Tauri, the token is read from a Tauri command on
// startup. In dev (plain browser), it falls back to the launchSettings
// token. Concepts: adapter pattern, dependency inversion from the UI to
// the shell. Called by TanStack Query hooks (added in Phase 3).

const DEV_TOKEN = 'dev-token-change-me';
// Mirrors tools/dev-ports.json → "api". When Tauri runs the WebView it
// overrides this via the get_api_base command, so this default only
// matters for `vite dev` outside the shell.
const DEV_API_BASE = 'http://127.0.0.1:5281';

export interface NucumShell {
  getApiBase(): Promise<string>;
  getToken(): Promise<string>;
  /** Phase 16 · extension v2: the browser extension answers some requests
   *  itself while Nücum is closed (src/extension/offline.ts). */
  fetch?: (url: string, init: RequestInit) => Promise<Response>;
}

// CONCEPT: Shell adapter.
// The UI must not know whether it is inside Tauri or a plain browser.
// The shell adapter abstracts "where is the API and what token do I use"
// so the fetch layer stays identical.
// WHY: Lets me test the UI with `vite dev` without Tauri running at all,
// and swap in the PWA shell in Phase 10.
// → Roadmap: Ay 3 · Faz 5 — Hexagonal Architecture
async function detectShell(): Promise<NucumShell> {
  // Tauri's window.__TAURI_INTERNALS__ is present when the WebView runs
  // under Tauri 2. In Phase 1 we only need to know "is this Tauri"; the
  // real `invoke` wiring is added together with the sidecar in task 7.
  // WHY `in`: an existence check narrows `window` without a cast; the
  // property is not on the Window type outside Tauri.
  if ('__TAURI_INTERNALS__' in window && window.__TAURI_INTERNALS__) {
    const { invoke } = await import('@tauri-apps/api/core');
    return {
      getApiBase: () => invoke<string>('get_api_base'),
      getToken: () => invoke<string>('get_session_token'),
    };
  }
  return {
    getApiBase: () => Promise.resolve(DEV_API_BASE),
    getToken: () => Promise.resolve(DEV_TOKEN),
  };
}

let shellPromise: Promise<NucumShell> | null = null;

/** The browser extension sets its own shell before the first request
 *  (src/extension/ext-shell.ts): the paired token, the port it found and
 *  the offline fallback. */
export function setShell(next: NucumShell): void {
  shellPromise = Promise.resolve(next);
}

function shell(): Promise<NucumShell> {
  shellPromise ??= detectShell();
  return shellPromise;
}

export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const s = await shell();
  const [base, token] = await Promise.all([s.getApiBase(), s.getToken()]);
  const headers = new Headers(init.headers);
  headers.set('Authorization', `Bearer ${token}`);
  if (!headers.has('Accept')) headers.set('Accept', 'application/json');
  return (s.fetch ?? fetch)(`${base}${path}`, { ...init, headers });
}

export async function apiJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await apiFetch(path, init);
  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText}`);
  }
  return response.json() as Promise<T>;
}

// Phase 10: the SignalR client opens its own connection (it cannot use
// apiFetch), so it needs the same base URL and token.
export async function apiConnection(): Promise<{ base: string; token: string }> {
  const s = await shell();
  const [base, token] = await Promise.all([s.getApiBase(), s.getToken()]);
  return { base, token };
}
