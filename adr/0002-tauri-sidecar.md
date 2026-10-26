# ADR 0002 — Tauri 2 + ASP.NET Core sidecar

Date: 2026-10-02 · Status: Accepted

## Context
The desktop app ships a .NET business logic layer and a React UI. We need a
shell that: (1) renders the WebView, (2) starts the .NET API as a child
process bound to localhost, (3) brokers a per-launch session token so the
API cannot be reached from other local processes.

## Decision
Use **Tauri 2** as the shell. The .NET API is bundled as a `sidecar`
binary via `tauri.conf.json → bundle.externalBin`. In dev the user runs
`dotnet run` manually (the shell trusts the port); in production the
shell spawns the sidecar with `NUCUM_PORT` and `NUCUM_TOKEN` env vars and
pipes stdout to the Rust process. Two Tauri commands (`get_api_base`,
`get_session_token`) expose the handshake values to the WebView.

## Alternatives
- **Electron:** 150 MB base, Node.js runtime baked into every install,
  weaker security defaults. We care about install size and crispness.
- **MAUI:** .NET-only, no mature WebView2 integration for React dev
  loop, Windows-first and still shaky.
- **Photino:** very small shell but no plugin ecosystem (no window-state,
  no single-instance, no global-shortcut) and no sidecar primitive.

## Consequences
- (+) Small installer (~15–20 MB), fast cold start.
- (+) Rust shell is tiny; most of the app's complexity stays in .NET.
- (+) Mature plugin set covers everything SPEC §20 asks for.
- (−) Our Rust layer is small but non-zero; a second toolchain for the
  developer.
- (−) The sidecar binary must be published self-contained (`PublishSingleFile`),
  which pushes the bundle up by ~70 MB on first install.
