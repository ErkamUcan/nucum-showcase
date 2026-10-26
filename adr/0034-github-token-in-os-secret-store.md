# ADR 0034 — The GitHub token lives in the Windows Credential Manager, never in the database

**Status:** Accepted · 2026-10-09 · Phase 11
**Related:** ADR 0029 (sync), ADR 0032 (backup/export), SPEC §9.2, §14 rule 4, DECISIONS S7

## Context

SPEC §9.2 connects a GitHub repository with a personal access token so the
day's commits show on Bugün and on the heatmap. A token is a password: whoever
holds it can act on the person's repositories. Everything in the SQLite file
travels: daily backups (zip), JSON/CSV/Excel exports, and (Phase 10) synced
rows to the phone.

## Decision

- **The token is stored only in the OS secret store.** On Windows: a generic
  credential in the Credential Manager (`CredWriteW`/`CredReadW`, encrypted
  with DPAPI for the logged-in Windows user). Target name
  `Nucum:github-token:<hash of the data folder>`, so dev, tests and the
  installed app never share a token.
- **It enters once** — in the body of `PUT /api/github/connection` to the
  localhost API — is checked against GitHub (`GET /user`, `GET /repos/{repo}`)
  and only then written. It is never returned by any endpoint, never logged,
  never in settings, backups, exports or the change log. A test scans the
  SQLite file for it.
- **Repo and login are ordinary settings** (`github.repo`, `github.login`).
- **Tests and screenshot runs** use an in-memory store
  (`Nucum:Secrets:Store = memory`) and a fake `HttpMessageHandler` behind the
  named client `github`; no test touches the real Credential Manager or
  network. On a non-Windows build the store reports "not available" and the
  connect form says so instead of falling back to plain text.
- **Failures degrade**: no network, a revoked token, an unknown repo or the
  rate limit become an error code in the status (`github.error.<code>` in the
  UI); stored commits stay visible; the next poll retries.
- **Rule 4 is now active**: new commits for a day raise
  `GitHubCommitsRecorded`; the engine runs rules with trigger `github.commit`
  once per day (`activity.log` "Kod yazdın"). Every commit is also an
  `activity_events` line `github.commit`. No routine is marked (DECISIONS S7).

## Alternatives considered

- **Encrypt the token with DPAPI and keep the blob in the database.** Still
  copied into every backup and export; useless on another machine and easy to
  forget when writing a new exporter.
- **Tauri secure storage (Stronghold / keyring plugin).** Only the WebView
  side could reach it; the .NET worker that polls GitHub would need the token
  passed in on every start.
- **GitHub OAuth device flow.** Better UX, but needs a registered OAuth app
  and client id; a personal token is what SPEC §9.2 asks for.

## Consequences

- Restoring a backup on another PC brings the commits but not the token: the
  person connects again (expected; the status says "not connected").
- Credential Manager behaviour is verified by hand (Phase 11 report: "elle
  denenmedi" until the user tries it); the logic around it is tested with the
  in-memory store.
