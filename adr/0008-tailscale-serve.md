# ADR 0008 — Tailscale Serve for phone access over HTTPS

Date: 2026-10-02 · Status: Accepted · **updated by ADR 0031** (Phase 10: native app, not a PWA)

## Context
Modern browsers require HTTPS for Service Workers (PWA offline storage,
install prompt, push). The phone must reach the desktop API from
anywhere the user carries the phone, not only on the home LAN.
Self-hosting a TLS cert on a home machine with a dynamic IP is a known
rabbit hole.

## Decision
Use **Tailscale Serve**: it exposes the local API on a trusted HTTPS
hostname inside the user's tailnet. The desktop app detects whether
Tailscale is installed and running, and either prompts the user to
install it or renders a QR code pointing at the Tailscale Serve URL.
Device pairing happens inside the app; devices get a **device token**
stored hashed in the DB and revocable from Settings.

## Alternatives
- **Local LAN only (http):** can't register a PWA.
- **Cloudflare Tunnel:** adds a third-party cloud dependency; the user
  prefers data staying on their machines.
- **Self-signed cert + QR code:** painful onboarding, breaks on each
  phone OS upgrade.

## Consequences
- (+) Zero-friction HTTPS, works from anywhere the phone is online.
- (+) Access control stays simple: pairing + device token + revoke.
- (+) No data leaves the user's own devices.
- (−) Adds a dependency on an external tool; we document the install.
- (−) Tailscale Serve has quirks (reconnect latency, port occupation);
  we add a healthcheck panel in Sync settings.
