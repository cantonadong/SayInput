# VoiceTyper portable toggle recording design

Date: 2026-09-15

## Goal

Turn VoiceTyper into a portable application with click-to-start/click-to-stop recording, deterministic sound feedback, temporary global output muting, a rolling local recording history, tray minimization, and unified application branding.

## Portable layout and migration

Runtime data lives beside `VoiceTyper.App.exe`:

```text
VoiceTyper.App.exe
data/settings.json
data/credentials/
recordings/*.wav
resources/icon.ico
resources/sounds/{start,stop,success,error}.wav
resources/sounds/README.md
```

Settings and DPAPI-encrypted credentials move from `%LOCALAPPDATA%/VoiceTyper` when the portable location is empty. Migration writes portable files atomically and leaves the old files untouched. The encrypted credential remains readable only by the same Windows user. A write failure is shown as a configuration error; secrets are never logged.

## Toggle hotkey state machine

Right Alt key-down edges toggle the session. Key-up and key-repeat events do nothing beyond suppressing the dedicated hotkey.

On the first click, the application plays `start.wav` to completion, snapshots the mute state of every active render endpoint, mutes those endpoints, captures the foreground target, and starts microphone recording. On the second click, it stops microphone capture, restores every endpoint to its captured mute state, plays `stop.wav`, and waits for recognition and injection.

Clicks during finalization or injection do not start an overlapping session. Once completion or failure returns the coordinator to idle, another click starts immediately. Shutdown, cancellation, startup failure, audio failure, and recognition failure all restore captured output state. Restoration is idempotent.

Successful text injection plays `success.wav`. A real capture, recognition, or injection failure plays `error.wav` and keeps the existing actionable notice. An empty final result is treated as a silent no-speech outcome: it shows no overlay error, tray notice, or error sound and immediately returns to idle. The user-initiated stop cue still plays after the output mute is restored.

Sound files are preloaded at startup and played without blocking the keyboard hook thread. A missing, malformed, or unplayable WAV is silently disabled; there is no system-sound fallback.

## Recording archive

The microphone PCM sent to recognition is also written as 16 kHz, 16-bit, mono WAV under `recordings/`. Each session writes a uniquely named temporary file and atomically renames it after capture stops. Sessions with captured PCM are retained even if recognition or injection later fails. Sessions with no PCM produce no file.

After finalizing a recording, cleanup orders completed WAV files by creation time and filename, retains the newest 20, and deletes older files. Temporary files from interrupted sessions are removed during startup cleanup. Filenames contain a local timestamp and random suffix, never transcript text or credentials.

## Window, icon, and tray behavior

Minimizing the settings window hides it and removes it from the taskbar while the tray icon remains available. Closing the settings window continues to leave the application running. The tray menu opens settings, toggles dictation, and exits.

`icon/icon.png` is converted into a multi-size Windows ICO and used for the executable, WPF windows, and WinForms tray icon. The PNG remains the editable source.

## Memory behavior

The settings window and recording overlay are created only when first needed. Background startup initializes configuration, hotkey, tray, sounds, and lightweight services without constructing the settings window or WPF overlay. Memory is measured for fresh background idle, settings visible, and after one recording. The goal is a material reduction from the current baseline, not parity with an application using a different runtime and UI stack.

## Components and boundaries

- `ToggleDictationController` serializes hotkey actions and owns the user-visible recording state.
- `SystemOutputMuteService` snapshots, mutes, and idempotently restores active render endpoints.
- `SoundCuePlayer` preloads and plays the four optional WAV resources.
- `RecordingArchive` creates WAV session writers and enforces the 20-file limit.
- `PortablePaths` provides executable-relative data and resource locations and performs one-time migration.
- Existing `DictationCoordinator` remains responsible for audio, recognition, target validation, and text injection. It reports no-speech separately from failures so presentation policy does not parse message strings.

## Verification

Automated tests cover toggle/debounce behavior, no overlapping sessions, mute snapshot restoration on every exit path, sound ordering and disabled invalid files, no-speech silence, WAV headers and PCM content, atomic archive completion, 20-file retention, portable path migration, and minimize-to-tray behavior where it can be isolated.

Manual validation covers audible cue order, system output muting/restoration, microphone capture, real Volcengine recognition, successful insertion, silent no-speech retry, tray minimization, portable relocation, archive cleanup, icon surfaces, and the three memory measurements.
