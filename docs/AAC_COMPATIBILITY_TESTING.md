# AAC-LC compatibility and robustness testing

This test plan combines automated decoder tests with real AAC-LC MP4 files produced by FFmpeg.

## Automated coverage

AacMp4AudioTrackDecoderTests verifies mono and stereo decoding at all 13 standard AAC sampling-frequency indices:

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

The decoder currently supports one- and two-channel AAC-LC playback. Tests also verify that unsupported multichannel layouts are rejected explicitly.

AacRawDataBlockReaderTests also truncates valid mono and stereo access units at every byte boundary and verifies that each incomplete prefix fails with EndOfStreamException, rather than being accepted as a complete frame.

## Generate the base compatibility matrix

Run this from the repository root in PowerShell:

~~~powershell
.\scripts\Generate-AacCompatibilitySamples.ps1
~~~

The script creates 4-second sine-wave MP4 samples in the system temporary directory. It always tests FFmpeg's native aac encoder and automatically adds libfdk_aac when that encoder is available in the installed FFmpeg build.

It generates mono and stereo files at each of the 13 standard sample rates. The generated media files are not added to Git.

## Generate bitrate, complexity, and duration stress samples

Run:

~~~powershell
.\scripts\Generate-AacCompatibilitySamples.ps1 -IncludeStressCases
~~~

The additional stress matrix focuses on 44.1 kHz and 48 kHz and varies:

- Mono and stereo output
- 48–128 kbit/s mono and 64–256 kbit/s stereo
- A pure tone, a multi-tone signal, and pink noise
- 30-second tracks by default

To increase the stress-track duration or choose another FFmpeg executable/output folder, use:

~~~powershell
.\scripts\Generate-AacCompatibilitySamples.ps1 -FfmpegPath "C:\Tools\ffmpeg\bin\ffmpeg.exe" -OutputDirectory "C:\Temp\Pixora-AAC" -IncludeStressCases -StressDurationSeconds 120
~~~

The stress matrix can generate many files, particularly when multiple AAC encoders are available.

## Manual playback checks

1. Run dotnet test PixoraStudio.slnx.
2. Open generated MP4s in Pixora Studio.
3. Play each file from beginning to end, especially the longer stress samples.
4. Seek repeatedly to the beginning, middle, and near the end; resume playback after each seek.
5. Check for decoder exceptions, playback stopping early, unexpected silence, clicks, distortion, or channel imbalance.
6. Confirm the reported sample rate and channel count match the file.

Keep the encoder name, sample rate, channel layout, bitrate, signal profile, and any exception text when reporting a failure.

## Current scope

This is AAC-LC in MP4 (mp4a) coverage. It does not claim support for HE-AAC/SBR, HE-AAC v2/PS, AAC Main, AAC SSR, AAC-LD, multichannel PCM output, or raw ADTS playback through the MP4 track decoder.
