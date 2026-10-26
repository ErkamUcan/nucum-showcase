# ADR 0043 — Live timer, share, shortcuts and widget as local native modules

**Status:** Accepted · 2026-10-19 · Mobile M11
**Related:** DECISIONS M50–M52, ADR 0041, `mobile/src/focus/timer.ts`

## Context

The user asked for four Android features on the phone: a live countdown in
the notification with buttons, "Paylaş → Nücum", launcher icon shortcuts
and a home-screen widget. None of them can be done in JavaScript alone.
Packages exist (`expo-share-intent`, `expo-quick-actions`,
`react-native-android-widget`), but each brings its own config plugin and
release cycle, has to match Expo SDK 57, and none shows a timer whose truth
stays in `timer.ts` (absolute times, ADR 0041 replica).

## Decision

Three small **local Expo modules** in `mobile/modules/` (Kotlin, Expo
Modules API, found by autolinking, built into the APK):

- **nucum-timer:** a foreground service (`specialUse`) draws the running
  timer with the system chronometer and Duraklat / Devam, Atla, Bitir.
  JS sends the whole picture (current phase and the ones after it) on
  every saved change; the service moves to the next phase by itself while
  JS sleeps. Button taps go to an **inbox** (SharedPreferences, with the tap
  time) and JS applies them to the saved state at that time. The OS alarms
  for phase ends stay as before.
- **nucum-launch:** reads text shared with `ACTION_SEND` (intent filter from
  `app.json`) and sets dynamic launcher shortcuts that open `nucum://`
  links.
- **nucum-widget:** an `AppWidgetProvider` that draws the last picture the
  app pushed (today's open items from `loadDay`, the running timer).

Later in M11 (DECISIONS M58–M61) the same approach added: a fourth module
**nucum-crypto** (PBKDF2-SHA256 for the desktop's journal PIN), the
distraction reminder inside the timer service, and widget refreshes at the
day cutover and at phase ends.

Every JS wrapper uses `requireOptionalNativeModule`, so jest, iOS and an
older build simply get "not available" and fall back (the plain expo
notification for the timer).

## Alternatives

- **The three packages:** less Kotlin here, but three more native
  dependencies to keep in step with Expo, and the timer would still need
  custom code.
- **Notification buttons that open the app:** no native code for taps, but
  "Duraklat" would pull the user into the app every time.
- **Rules in Kotlin** (widget computes today itself): a second copy of
  TodayService that would drift from the TypeScript twin.

## Consequences

- No new third-party dependency; about 500 lines of Kotlin that the user
  can read (comments follow project rules).
- A tap made while Android has stopped the app is applied when the app runs
  again. The tap cancels expo's phase-end alarms by calling expo's public
  NotificationsService by name (reflection), because the prebuilt
  expo-notifications library cannot be linked without pinning its version.
- The widget shows the last pushed picture, which includes tomorrow's list
  and the phases ahead, so it can move on by itself.
- Native changes need a new APK (`npm run mobile:android` / `mobile:apk`);
  JS changes still arrive through Metro.
