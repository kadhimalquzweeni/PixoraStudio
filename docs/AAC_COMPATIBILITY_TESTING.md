# AAC-LC compatibility test matrix

This matrix supplements the automated unit tests with real AAC-LC MP4 files produced by FFmpeg.

## Automated coverage

`AacMp4AudioTrackDecoderTests` verifies mono and stereo decoding at all 13 standard AAC sampling-frequency indices:

| Index | Sample rate |
|---:|---:|
| 0 | 96,000 Hz |
| 1 | 88,200 Hz |
| 2 | 64,000 Hz |
| 3 | 48,000 Hz |
| 4 | 44,100 Hz |
| 5 | 32,000 Hz |
| 6 | 24,000 Hz |
| 7 | 22,050 Hz |
| 8 | 16,000 Hz |
| 9 | 12,000 Hz |
| 10 | 11,025 Hz |
| 11 | 8,000 Hz |
| 12 | 7,350 Hz |

The decoder currently supports one- and two-channel AAC-LC playback. Tests also verify that channel configurations resolving to 3, 4, 5, 6, or 8 channels are rejected explicitly.

## Generate real encoder fixtures

Run this from the repository root in PowerShell:

```powershell
.\scripts\Generate-AacCompatibilitySamples.ps1
```

The script creates 4-second sine-wave MP4 samples in the system temporary directory. It always tests FFmpeg's native `aac` encoder and automatically adds `libfdk_aac` when that encoder is available in the installed FFmpeg build.

It generates mono and stereo files at each of the 13 standard sample rates. The generated media files are not added to Git.

To choose another output folder or FFmpeg executable:

```powershell
.\scripts\Generate-AacCompatibilitySamples.ps1 -FfmpegPath "C:\Tools\ffmpeg\bin\ffmpeg.exe" -OutputDirectory "C:\Temp\Pixora-AAC"
```

## Manual playback checks

1. Run `dotnet test PixoraStudio.slnx`.
2. Open each generated MP4 in Pixora Studio.
3. Play each file from beginning to end.
4. Check for decoder exceptions, playback stopping early, silence, clicks, distortion, or channel imbalance.
5. Confirm the reported sample rate and channel count match the file.

Keep the encoder name, sample rate, channel layout, and any exception text when reporting a failure. This makes it possible to distinguish an encoder-specific bitstream issue from a sample-rate or channel-layout issue.

## Current scope

This is AAC-LC in MP4 (`mp4a`) coverage. It does not claim support for HE-AAC/SBR, HE-AAC v2/PS, AAC Main, AAC SSR, AAC-LD, multichannel PCM output, or raw ADTS playback through the MP4 track decoder.
