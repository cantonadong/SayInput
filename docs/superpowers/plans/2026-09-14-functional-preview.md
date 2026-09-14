# Functional Preview Implementation Plan

**Goal:** Complete the existing Windows voice input specification through a self-contained test build.
**Spec:** DEVELOPMENT.md. User authorized continuing through all remaining tasks on 2026-09-14; Task 4 manual acceptance passed.
**Architecture:** Existing Core interfaces, Windows adapters and Volcengine provider. Capture starts before connection; one bounded ordered audio pipeline; final-only injection.
**Tech stack:** .NET 8, WPF, NAudio WASAPI, ClientWebSocket, DPAPI.

Use executing-plans task by task, with runnable checks for nontrivial behavior. Existing workspace is retained to preserve user changes and local test packages.

- [ ] 5: AudioWarmupService enumerates devices and initializes 16 kHz PCM using native WASAPI conversion. Optional device warm-up always releases capture; enumeration-only is default.
- [ ] 6: PcmRingBuffer has fixed storage, ordered read/reset/overwrite checks. The live pipeline never overwrites pending speech: a bounded channel holds up to 10 seconds, then reports overflow.
- [ ] 7: WasapiAudioCaptureService selects default/device ID, uses event-driven shared capture and native PCM conversion; stop awaits callbacks. Test level calculation and real device repeated start/stop where available.
- [ ] 8: PerformanceMetrics records first monotonic milestones, immutable snapshots, clearing. No transcript or credentials in diagnostics.
- [ ] 9: SeedProtocol implements official framing with bounded length/decompression and malformed/error/final fixtures.
- [ ] 10: VolcengineStreamingRecognizer uses cancellable connection, serialized sends and one receive loop, final-only completion and disposal. Local WebSocket integration check.
- [ ] 11: AudioPipeline buffers 0/100/300/800 ms connection delays without losing initial PCM; overflow aborts. Stop drains before final.
- [ ] 12: Overlay is topmost/no-activate/click-through and session-tagged; displayed only after audio start.
- [ ] 13: RMS/peak smoothed bars, at most 30 Hz while visible; rendering unsubscribes on hide.
- [ ] 14: Batch Unicode INPUT construction, surrogate/newline checks, target validation.
- [ ] 15: Clipboard fallback uses a dedicated STA thread, bounded retries, conditional restoration, no fallback after partial SendInput delivery.
- [ ] 16: Coordinator tests duplicate down, release during Starting, cancellation, stale partials, failures, empty final, invalid target, delayed connection and exactly-once injection.
- [ ] 17: Tray, named mutex and shutdown cleanup; no idle polling or microphone/socket.
- [ ] 18: Settings, DPAPI credentials, microphone meter, connection test, startup registration and enable controls. Cloud audio disclosure.
- [ ] 19: Release profiling and real measurement report; distinguish hardware/cloud/manual tests from simulations.
- [ ] 20: Compatibility checklist and automated native injection check; record untested applications honestly.
- [ ] 21: Release publish to artifacts/voice-test, startup/shutdown smoke test, README instructions and final verification.

Short release during Starting is latched and finalized immediately after audio initialization. A 10-second audio backlog is a deliberate bounded ceiling; overflow fails visibly. Native audio initialization cannot recover sound before hardware begins capture. Focus restoration is attempted only for a still-valid original window and rechecked before injection.
