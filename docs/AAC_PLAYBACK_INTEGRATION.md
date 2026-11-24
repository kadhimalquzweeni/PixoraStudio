# AAC Playback Integration

## Playback architecture

Pixora keeps LibVLC as the video renderer and uses the custom MediaEngine AAC-LC decoder for supported MP4 audio tracks.

- LibVLC decodes and renders the video stream.
- `AacAudioPlaybackService` parses the MP4 audio track and decodes AAC-LC to PCM.
- NAudio `WaveOutEvent` plays the PCM WAV output through the Windows audio device.
- LibVLC's audio output is muted while custom AAC playback is active, preventing duplicate audio.
- Unsupported audio tracks or a custom output startup failure fall back to LibVLC audio.

Custom AAC playback currently supports AAC-LC mono and stereo. Other codecs and channel layouts remain on the LibVLC fallback path.

## Playback controls

- Play and pause are applied to both video and custom audio.
- The volume slider controls NAudio output when custom AAC is active and LibVLC volume on the fallback path.
- The mute control affects the active output without changing the selected volume level.
- Video close, view unload, and replacement loads cancel outstanding work, stop audio output, dispose readers, and remove temporary WAV files.

## Seeking and synchronisation

A user seek no longer only repositions the already-decoded WAV reader.

1. The service parses the source MP4 and locates its AAC audio track.
2. `Mp4AudioSeekPlanner` selects the presentation sample containing the target time and includes the preceding decode-order sample as AAC overlap-add preroll.
3. `AacMp4AudioSeekDecoder` decodes from the preroll sample, discards PCM before the target, and continues decoding the remaining access units.
4. Samples excluded by the edit list are decoded to preserve AAC synthesis state but are not placed on the output timeline.
5. `PcmAudioTimelineAssembler` places the decoded blocks using their presentation timestamps, filling gaps with silence and trimming overlaps.
6. The service writes a temporary PCM WAV segment beginning at the requested presentation time and replaces the active NAudio reader.
7. When playback was active, the video is paused during the rebuild and both clocks resume together when the segment is ready.

While playing, the WPF playback timer compares the NAudio position with LibVLC's video position. If drift exceeds the synchronisation threshold, the service rebuilds the audio segment from the current video time.

The audio position exposed by the service includes the segment's presentation-time offset, so the UI and synchronisation logic continue to use the source video's timeline rather than a segment-relative clock.

## Automated coverage

`AacMp4AudioSeekDecoderTests` exercises presentation-time seeking with synthetic mono and stereo AAC-LC MP4 tracks. It verifies that the decoder restores preroll state, trims the requested frames from the target access unit, and assembles subsequent PCM on the requested timeline.

The existing MediaEngine test suite also covers:

- AAC seek planning and invalid seek plans
- AAC access-unit decoding and truncation handling
- PCM gaps, overlaps, negative preroll, and timeline assembly
- MP4 presentation timestamps and edit-list validation

## Manual Windows playback checks

Run the Release test suite first:

```powershell
dotnet test .\PixoraStudio.slnx --configuration Release
```

Then verify with the AAC compatibility samples and representative MP4 videos:

1. Open an AAC-LC mono MP4 and confirm audio and video start together.
2. Repeat with AAC-LC stereo.
3. Pause and resume several times; confirm neither stream continues while paused.
4. Seek to approximately 10%, 50%, and 90% while paused and while playing.
5. Drag the timeline playhead while playing; confirm playback resumes from the selected position without a large audio/video offset.
6. Repeat forward and backward seeks rapidly to check that superseded decode tasks are cancelled.
7. Change volume, mute, and unmute during playback and while paused.
8. Let the video reach the end, then restart playback and confirm audio restarts from the beginning.
9. Close a video during initial decode and during a seek; confirm no stale audio starts and temporary files are cleaned up.
10. Open a non-AAC MP4 and confirm LibVLC audio fallback still works.

## Current trade-off

Seeking currently rebuilds a temporary PCM segment from the target position through the end of the audio track before resuming. This keeps the implementation deterministic and reuses the tested MediaEngine seek/timeline components, but seek latency can increase with long clips. A bounded streaming PCM queue is a future optimisation; it is not required for correctness of the current segment-based playback path.
