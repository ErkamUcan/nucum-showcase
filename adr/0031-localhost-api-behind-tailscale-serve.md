# ADR 0031 — The API stays on localhost; the phone reaches it through Tailscale Serve

**Status:** Accepted · 2026-10-08 · Phase 10
**Updates:** ADR 0008 (written for a PWA; the phone is now a native app, M1/M7)

## Context

The phone app must reach the desktop at home and outside, over HTTPS, with
nothing paid (DECISIONS M8). The API listens on `127.0.0.1:5281` only
(DECISIONS R5): no other machine on the LAN can open a socket to it.

## Decision

- **Kestrel keeps `ListenLocalhost`.** Nücum never opens a LAN port and never
  binds `0.0.0.0`.
- **Tailscale Serve is the only door.** The user runs once:
  `tailscale serve --bg 5281`. The Tailscale daemon terminates TLS with a
  real certificate for `https://<computer>.<tailnet>.ts.net` and forwards to
  `http://127.0.0.1:5281`. Only devices logged into the same tailnet can
  resolve and reach that name (Serve, not Funnel — Funnel would publish it
  to the internet).
- **Checked, not configured, by the app.** Ayarlar → Telefon runs
  `tailscale status --json` and `tailscale serve status --json` (3 s
  timeout) and shows: installed? logged in? serve pointing at our port? The
  app does not run `tailscale serve` itself — changing the user's network
  setup is their explicit step, shown as a copyable command.
- **Defense in depth.** Even inside the tailnet every sync request needs a
  device token (ADR 0030); desktop routes still need the session token.

## Alternatives considered

- **Bind to the LAN IP with a self-signed certificate.** Works only at home,
  the phone must trust a custom CA, and every LAN device can reach the port.
- **Cloudflare Tunnel / ngrok.** Public URL, third-party relay, accounts;
  some features paid.
- **Tailscale Funnel.** Public internet exposure; not needed for one phone.

## Consequences

- Tailscale (free personal plan) is a prerequisite for the phone; the guide
  lives in the app and in the Phase 10 report.
- In Serve, requests arrive from `127.0.0.1`, so the API cannot tell a phone
  from a local process by address — which is why authentication is by token,
  never by IP.
- If the user changes the API port (`tools/dev-ports.json`), the Serve
  command must change too; the status check compares the proxied port.

- **Host names (2026-10-26, F163):** Serve forwards the request with `Host: <computer>.<tailnet>.ts.net`, so `AllowedHosts` must list `*.ts.net` next to localhost. Until 0.18.3 it did not, and a real phone got "Invalid Hostname" (400) on its first request.
