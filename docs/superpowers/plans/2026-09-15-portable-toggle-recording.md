# Portable Toggle Recording Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Deliver a portable VoiceTyper build with click-to-toggle recording, ordered optional sound cues, reversible global output muting, a rolling 20-file WAV archive, tray minimization, unified icons, and lower idle memory.

**Architecture:** Keep `DictationCoordinator` responsible for one speech session and place user interaction sequencing in a new toggle controller. Add focused adapters for portable paths, output muting, cue playback, and WAV archival, all injected behind small interfaces so state transitions and cleanup can be tested without hardware.

**Tech Stack:** .NET 8, C#, WPF, WinForms NotifyIcon, NAudio/CoreAudio, xUnit, MSBuild content resources.

**Spec:** `docs/superpowers/specs/2026-09-15-portable-toggle-recording-design.md`

## Global Constraints

- Runtime settings, DPAPI credentials, recordings, icons, and sounds live beside the executable in their specified folders.
- Missing or invalid sound files stay silent; there is no system-sound fallback.
- Output mute state is restored on stop, failure, cancellation, and shutdown.
- Empty/no-speech sessions show no notice and play no error cue.
- Keep at most 20 completed WAV recordings and never put transcript text or credentials in filenames.
- Preserve existing workspace changes and never blanket-stage generated files.

---

### Task 1: Optional sound cues and resources

**Files:**
- Create: `src/VoiceTyper.App/Sound/DictationSoundFeedback.cs`
- Create: `src/VoiceTyper.App/Sound/WaveSoundCuePlayer.cs`
- Create: `tests/VoiceTyper.Windows.Tests/SoundFeedbackTests.cs`
- Modify: `src/VoiceTyper.App/Bootstrap/DictationRuntime.cs`
- Modify: `src/VoiceTyper.App/VoiceTyper.App.csproj`
- Copy: `resources/sounds/*.wav`, `resources/sounds/README.md`

**Interfaces:** Produces `SoundCue`, `ISoundCuePlayer.Play(SoundCue)`, and `DictationSoundFeedback` transition methods.

- [x] Write tests proving start/stop are emitted once per session, success/error map correctly, and invalid resources stay silent.
- [x] Run `dotnet test ... --filter FullyQualifiedName~SoundFeedbackTests` and confirm the missing behavior fails.
- [x] Implement preloaded `SoundPlayer` instances with silent failure and wire coordinator/immediate-error events.
- [x] Copy WAV resources and configure them as output content.
- [x] Run the focused tests and confirm they pass.

### Task 2: Portable settings and credential migration

**Files:**
- Create: `src/VoiceTyper.App/Storage/PortablePaths.cs`
- Modify: `src/VoiceTyper.App/Settings/ConfigurationService.cs`
- Modify constructors in `src/VoiceTyper.Windows/Settings/JsonSettingsStore.cs` and `src/VoiceTyper.Windows/Security/DpapiCredentialStore.cs` only as needed for explicit portable paths.
- Create: `tests/VoiceTyper.Windows.Tests/PortablePathsTests.cs`

**Interfaces:** Produces executable-relative `DataDirectory`, `SettingsPath`, `CredentialDirectory`, `RecordingDirectory`, and `ResourceDirectory`.

- [x] Write tests for path resolution, migrate-only-when-empty behavior, atomic copies, and unchanged legacy files.
- [x] Run focused tests and confirm failure.
- [x] Implement portable path creation and first-run migration, then inject explicit stores into `ConfigurationService`.
- [x] Run focused tests and confirm pass.

### Task 3: Reversible system output mute

**Files:**
- Create: `src/VoiceTyper.Windows/Audio/SystemOutputMuteService.cs`
- Create: `src/VoiceTyper.Core/Audio/ISystemOutputMuteService.cs`
- Create: `tests/VoiceTyper.Windows.Tests/Audio/SystemOutputMuteTests.cs`

**Interfaces:** Produces `Task MuteAsync(CancellationToken)` and `Task RestoreAsync()` with idempotent restoration.

- [x] Write adapter-boundary tests proving original muted/unmuted states are restored and repeated restore is harmless.
- [x] Run tests and confirm failure.
- [x] Implement CoreAudio render-endpoint snapshot, mute, rollback-on-partial-failure, and restore.
- [x] Run focused tests and confirm pass.

### Task 4: Click-to-toggle orchestration

**Files:**
- Create: `src/VoiceTyper.App/Bootstrap/ToggleDictationController.cs`
- Modify: `src/VoiceTyper.App/Bootstrap/DictationRuntime.cs`
- Modify: `src/VoiceTyper.Windows/Keyboard/RightAltHotkeyService.cs` only if release subscription cleanup requires it.
- Create: `tests/VoiceTyper.Windows.Tests/ToggleDictationControllerTests.cs`

**Interfaces:** Consumes coordinator start/release, mute service, and cue feedback; produces `ToggleAsync()` and `StopAsync()`.

- [x] Write tests for first-click start ordering, second-click stop ordering, ignored repeats/finalization clicks, failure restore, shutdown restore, and immediate retry after no speech.
- [x] Run focused tests and confirm failure.
- [x] Implement a serialized async command gate; keyboard callbacks only enqueue work and never block.
- [x] Remove key-up-to-release behavior and wire shutdown through `StopAsync()`.
- [x] Run focused tests and confirm pass.

### Task 5: Silent no-speech outcome

**Files:**
- Modify: `src/VoiceTyper.Core/Dictation/DictationCoordinator.cs`
- Modify: `src/VoiceTyper.App/Bootstrap/DictationRuntime.cs`
- Modify: `tests/VoiceTyper.Core.Tests/CoordinatorTests.cs`
- Modify: `tests/VoiceTyper.Windows.Tests/SoundFeedbackTests.cs`

**Interfaces:** Finished event distinguishes no-speech from an actual error without parsing display strings.

- [x] Write tests proving empty final returns idle with no error/notice/error cue and accepts the next start.
- [x] Run focused tests and confirm failure.
- [x] Introduce an explicit completion outcome and update runtime presentation.
- [x] Run focused tests and confirm pass.

### Task 6: Rolling WAV recording archive

**Files:**
- Create: `src/VoiceTyper.Core/Audio/IRecordingArchive.cs`
- Create: `src/VoiceTyper.Windows/Audio/WaveRecordingArchive.cs`
- Modify: `src/VoiceTyper.Core/Audio/AudioPipeline.cs`
- Create: `tests/VoiceTyper.Windows.Tests/Audio/WaveRecordingArchiveTests.cs`
- Modify: `tests/VoiceTyper.Core.Tests/PipelineTests.cs`

**Interfaces:** Produces a session writer accepting PCM chunks and asynchronous complete/abort operations.

- [x] Write tests for valid WAV headers/data, no-file empty sessions, atomic completion, interrupted temp cleanup, and newest-20 retention.
- [x] Run focused tests and confirm failure.
- [x] Implement executable-relative archive and tee the exact ASR PCM through the pipeline.
- [x] Run focused tests and confirm pass.

### Task 7: Tray minimization and lazy WPF allocation

**Files:**
- Modify: `src/VoiceTyper.App/MainWindow.xaml.cs`
- Modify: `src/VoiceTyper.App/App.xaml.cs`
- Modify: `src/VoiceTyper.App/Bootstrap/DictationRuntime.cs`
- Modify: `src/VoiceTyper.App/Overlay/RecordingOverlay.xaml.cs`
- Create: `tests/VoiceTyper.Windows.Tests/WindowLifecycleTests.cs`

**Interfaces:** Settings minimize requests hide the window; overlay is provided by a lazy wrapper implementing `IRecordingOverlay`.

- [x] Write tests for minimize-to-hide and deferred overlay construction.
- [x] Run focused tests and confirm failure.
- [x] Wire `StateChanged`, hide minimized settings, and add lazy overlay delegation with safe disposal.
- [x] Run focused tests and confirm pass.

### Task 8: Unified multi-size icon

**Files:**
- Create: `icon/icon.ico`
- Modify: `src/VoiceTyper.App/VoiceTyper.App.csproj`
- Modify: `src/VoiceTyper.App/MainWindow.xaml`
- Modify: `src/VoiceTyper.App/Tray/TrayIconService.cs`

**Interfaces:** The executable, WPF window, and tray load the same ICO resource.

- [x] Generate 16, 24, 32, 48, 64, 128, and 256 pixel ICO frames from `icon/icon.png`.
- [x] Set `ApplicationIcon`, WPF `Icon`, and tray icon loading with owned icon disposal.
- [x] Build and inspect executable and window/tray icon surfaces.

### Task 9: Integration, portable migration, and memory validation

**Files:**
- Modify: `docs/development-progress.md`
- Modify: `docs/performance.md`
- Modify: `docs/compatibility.md`

**Interfaces:** Produces the final self-contained package under `artifacts/voice-test`.

- [x] Run all Release non-hardware tests and require zero failures.
- [x] Stop the old EXE, publish self-contained win-x64, and restart because SimpleWall rules apply to the restarted process.
- [ ] Verify portable data migration, cue order, toggle recording, mute restoration, silent no-speech retry, successful real recognition, 20-file cleanup, tray minimization, and all icon surfaces.
- [ ] Measure post-recording working set/private bytes. Background idle and settings-visible measurements are complete.
- [x] Update progress, performance, and compatibility documents with exact results and remaining limitations.
