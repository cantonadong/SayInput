# Sound Resources

This folder contains custom notification sounds for LazyTyper.

## Sound Files

- **start.wav** - Recording start (two-note ascending, warm silver digital synth)
- **stop.wav** - Recording stop / processing (subtle, low-presence transition)
- **success.wav** - Transcription complete (two-note ascending, warm completion)
- **error.wav** - Error / cancel (two-note descending, gentle warning)

## Platform Support

Custom WAV sounds are used on both macOS and Windows. The implementation will:

1. First attempt to play the custom sound (preloaded at startup)
2. Fall back to system alert sound if the file is not found

## Technical Details

- Format: WAV (PCM 16-bit, 44.1kHz, Stereo)
- The files are bundled with the application during build
- Location in production: `resources/sounds/` relative to the executable
