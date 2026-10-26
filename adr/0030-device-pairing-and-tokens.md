# ADR 0030 — Device pairing by one-time code, hashed device tokens

**Status:** Accepted · 2026-10-08 · Phase 10

## Context

The desktop API already protects every route with the per-launch session
token (DECISIONS R4); that token rotates on each launch and must never leave
the machine. A phone needs a credential that survives restarts, can be
revoked alone, and is useless if the database file leaks.

## Decision

- **Pairing code.** Ayarlar → Telefon asks for a code: 8 characters from a
  32-letter alphabet without look-alikes (≈ 40 bits), valid
  `sync.pairingCodeMinutes` (default 5), **single use**. Only its SHA-256
  hash is stored. The QR holds `{v, url, code}`; the code can also be typed.
- **Exchange.** `POST /api/sync/pair {code, deviceName, platform}` (the only
  anonymous sync route) marks the code used and returns a **device token**
  (32 random bytes, base64url) once. The desktop keeps only `SHA-256(token)`
  with a unique index; lookup is by hash, so there is no string compare to
  time.
- **Two credentials, two route families.** `/api/sync/*` and the device
  SignalR hub `/hubs/sync` accept only a device token (endpoint metadata
  tells `SessionTokenMiddleware` to step aside; `DeviceTokenFilter` checks).
  `/api/phone/*` (QR, devices, conflicts, Tailscale) and the desktop hub
  `/hubs/desktop` stay on the session token. A device token never opens a
  desktop route and the session token never opens a sync route.
- **Revoke.** Sets `RevokedAtUtc` (the row stays for the history); the next
  request answers 401 `sync.device_revoked` and the device's live hub
  connections are told to stop. Pairing again makes a new device row.

## Alternatives considered

- **JWT.** Self-contained, but revocation then needs a deny list anyway, and
  a single desktop has nothing to gain from stateless validation.
- **Reuse the session token.** It rotates per launch (the phone would break
  daily) and revoking it would log out the desktop UI.
- **Store the token encrypted (Data Protection).** Needed when the secret
  must be read back (GitHub token); a bearer token only has to be compared,
  so a hash is enough and safer.

## Consequences

- A lost token cannot be shown again; the device pairs anew.
- Pairing codes are tried at most `sync.pairingMaxAttempts` times across
  all clients within one code's life; after that the code is burned.
- Media chunks, push and pull all authenticate the same way, so one filter
  covers the whole device surface (tested with a revoked token).
