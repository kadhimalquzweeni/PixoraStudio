# MP4 audio timing and seeking

## Timing model

MP4 audio samples carry decode timestamps, durations, and optional composition offsets. The resolved presentation timeline converts those values into seconds using the track timescale. PCM placement uses sample frames rather than floating-point sample positions so gaps, overlaps, and negative preroll can be handled without accumulating rounding drift.

## Seeking

`Mp4AudioSeekPlanner` maps a requested presentation time to the sample covering that time. If the requested time falls into a gap, it selects the next sample. Times beyond the end of the track are clamped to the final sample.

AAC-LC synthesis uses overlap-add state across access units. A seek plan therefore includes the preceding decode-order sample as preroll (when one exists). Pass the plan to `AacMp4AudioTrackDecoder.DecodeSamplesFrom`: it resets synthesis state, decodes the preroll sample to rebuild overlap state, discards that preroll PCM, trims the requested number of leading frames from the target sample, and yields the remaining PCM buffers.

The planner currently assumes the configured AAC access-unit output size supplied by the caller (1024 frames by default). Pass the decoded stream sample rate and the actual access-unit frame count if a future supported AAC profile uses a different frame length.

## Automated coverage

- Presentation-time to sample selection, including gaps, boundaries, track filtering, and out-of-range times.
- AAC overlap-add preroll and PCM trimming for mono and stereo.
- Existing PCM placement tests for contiguous blocks, gaps, overlaps, negative preroll, fully covered blocks, and integer overflow.
- Existing MP4 timeline tests for timescale conversion, composition offsets, edit lists, and presentation ordering.
- Existing MP4 sample-location tests for decode timestamps, durations, and composition timestamps.

Run the full suite from the repository root:

```powershell
dotnet test .\PixoraStudio.slnx --configuration Release
```

## Manual checks

Use the generated AAC compatibility files and stress samples:

1. Play each file from the beginning and listen for gaps, clicks, or distortion.
2. Seek to approximately 10%, 50%, and 90% of the duration.
3. Resume playback after each seek and listen for a transient click, brief silence, or duplicated audio.
4. Seek repeatedly backwards and forwards, including near the beginning and end.
5. Repeat with mono, stereo, pink-noise, and multi-tone samples.

The seek planner and decoder API are media-engine capabilities; the WPF playback pipeline must call the seek API and reposition its output queue when wiring interactive timeline seeking.
