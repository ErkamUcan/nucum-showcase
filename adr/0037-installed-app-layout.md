# ADR 0037 — The installed Windows app: per-user installer, own port and data folder, pre-upgrade backup

**Status:** Accepted · 2026-10-09 · Phase 12
**Related:** ADR 0002 (Tauri + sidecar), ADR 0031 (localhost + Tailscale Serve), ADR 0032 (backups), DECISIONS F59–F60, F67–F69

## Context

Until Phase 12 the app only ran from the repository (`npm run dev`): API on
5281, database `backend/src/Nucum.Api/nucum.db`. An installed copy must
live next to a development copy on the same machine, survive updates
without losing data, and install without paid code signing or admin rights.

## Decision

- **Installer:** Tauri bundler, NSIS, `installMode: currentUser` — installs
  under the user's profile, no UAC prompt. Not code-signed (paid);
  SmartScreen shows "Windows kişisel bilgisayarınızı korudu" the first time
  (README explains "Ek bilgi → Yine de çalıştır"). WebView2 is fetched by
  the bootstrapper if missing (Windows 11 has it).
- **API sidecar:** `dotnet publish` single file, self-contained, native
  SQLite inside (`IncludeNativeLibrariesForSelfExtract`), copied as
  `Nucum.Api-<target triple>.exe` (`tools/build-win.mjs`).
  `appsettings.json` ships as a bundle resource next to it; when installed
  the API uses the exe's folder as content root.
- **Port:** installed 5291, development 5281 (`tools/dev-ports.json`
  `installed`, checked by `check-ports.mjs`). Fixed, not random, so the
  phone's `tailscale serve` command stays valid across restarts.
- **Data folder:** the shell passes `NUCUM_DATA_DIR=%APPDATA%\Nucum`; the API
  puts the database there and derives backups, exports, media and logs
  from it. Development keeps its own file. `NUCUM_DATA_DIR` can be set by
  hand for a portable copy. The personal pack is looked up in
  `%APPDATA%\Nucum\packs\personal` (walking up from the install folder would
  find nothing — or, for a build run inside the repository, the developer's
  own pack, which the Phase 12 smoke test showed).
- **Start-up:** the main window is created hidden and shown when
  `GET /health` answers (30 s cap). The shell kills the sidecar on exit.
- **Version stamp + pre-upgrade backup:** the app version (one value in five
  files, `tools/check-version.mjs`) is written into the database header
  (`PRAGMA user_version`) after the migrations. At start-up, a database with
  a different stamp (or none, i.e. older than Phase 12) gets a full backup
  of kind `pre-upgrade` BEFORE any migration runs (`UpgradeGuard`). It is
  listed and restorable in Ayarlar → Veri and never pruned.
- **Autostart:** `tauri-plugin-autostart` (per-user Run key), setting
  `desktop.autostart`, default off; started with `--autostart` the shell
  stays in the tray.
- **No auto-update** (DECISIONS F68, user decision 2026-10-10): the Tauri
  updater would need a GitHub token inside the installer to read a private
  repository's releases. A new version is installed over the old one by
  hand; the pre-upgrade backup protects the data.
- **WebView reaches the API (added after the first install, DECISIONS
  F72):** Chromium's Local Network Access check (WebView2 142+) treats a
  request from `http://tauri.localhost` to `127.0.0.1` as a permission
  request and WebView2 denies it without a prompt. The main window's
  `additionalBrowserArgs` disables `LocalNetworkAccessChecks` (keeping
  wry's defaults); the mini and quick windows copy the same arguments
  from the config, because WebView2 refuses a second window with other
  arguments. Alternatives were serving the UI from the API (same origin,
  but Tauri IPC then needs remote-URL capabilities) or proxying every call
  through a Rust command. `npm run test:smoke` (a step of `build:win`)
  runs the production build + packaged API from the real origin, in the
  installed Microsoft Edge (closest to WebView2).
- **Second cause, found on reinstall:** the shell's `get_api_base` and
  `get_session_token` commands took a `State<'_, ()>` that was never
  managed, so Tauri rejected every call and the page never sent a request.
  The argument is gone; `npm run test:shell` calls both commands through
  Tauri's IPC on the mock runtime (the test exe gets the Common Controls v6
  manifest from build.rs, as the app does).

## Alternatives

- **MSI (WiX):** per-machine by default, needs admin; NSIS per-user is
  friendlier for a personal app.
- **Random port per start (R1 option):** breaks the Tailscale Serve mapping.
- **Data next to the exe:** lost on uninstall/update, and Program Files is
  read-only.
- **EF Core migrations history table for versions:** the app uses
  `ModuleSchema` (desired-state schema sync); the header stamp is all it
  needs and costs no table.

## Consequences

- Development and installed copies can run side by side.
- An update never migrates a database without a restorable copy first.
- Uninstalling leaves `%APPDATA%\Nucum` in place (user data is never
  deleted silently); README says how to remove it by hand.
- Tray, autostart, window and installer behaviour are tested by hand
  (project rules); the report lists the steps.
