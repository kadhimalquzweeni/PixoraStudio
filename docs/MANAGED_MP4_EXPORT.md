# Managed MP4 Export

PixoraStudio's MP4 export path must remain fully managed. It must not invoke FFmpeg, FFprobe, external executables, or shell processes.

## Design

The export pipeline is being built in layers:

1. **Trim planning** — resolve the requested timeline range into decode-order samples for each supported track.
2. **Container writing** — write MP4 boxes, recursively rebuild containers, and regenerate sample tables.
3. **Media remuxing** — copy the original encoded sample payloads into a new `mdat` and patch chunk offsets.
4. **Track/movie metadata** — update durations and generate timeline edit metadata.
5. **Application integration** — expose export progress/cancellation and connect the exporter to the video editor.
6. **End-to-end validation** — parse exported files with PixoraStudio's own reader and decode representative video/audio samples.

## Current implementation

`Mp4TrimExportPlanBuilder` implements the first layer. It:

- validates finite, positive trim ranges within the movie duration;
- requires a video track and a usable sync sample;
- begins on a sync sample at or before the requested start (or the first later sync sample when no earlier one exists);
- ends at the next sync sample at or after the requested end, treating that sample as an exclusive GOP boundary;
- selects audio samples whose presentation intervals overlap the effective video range;
- preserves contiguous decode-order sample selections and rejects discontinuous audio edit-list selections.

The plan reports both the requested and effective ranges. Since this is a remuxing path rather than a re-encoder, the effective range is GOP-aligned and can be wider than the user's requested range. The UI must show this accurately; it must not claim frame-accurate trimming.

The current planner explicitly rejects source video/audio edit lists. Supporting those correctly requires translating the source presentation timeline into new output edit lists and is deferred until the track/movie metadata stage.

The following managed building blocks are now present:

- `Mp4BoxWriter` writes normal and extended-size ISO BMFF boxes.
- `Mp4BoxTreeWriter` rebuilds parsed containers while preserving unchanged leaf boxes byte-for-byte and supports full-box replacement.
- `Mp4SampleTableBoxWriter` writes sample timing, chunk mapping, sizes, sync samples, composition offsets and 32-/64-bit chunk offsets.
- `Mp4MediaDataWriter` streams selected encoded sample bytes into a new extended-size `mdat` and returns their absolute output offsets.

These components are individually tested. They do not yet constitute a complete file exporter because the top-level writer must combine them with correctly recalculated track/movie durations and output edit lists.

## Container writer requirements

The writer must rebuild, not blindly copy, all timing and sample-location metadata affected by trimming:

- `stts`, `ctts`, `stss`, `stsz`, `stsc`, and `stco`/`co64`;
- `mdhd`, `tkhd`, and `mvhd` durations;
- edit lists when they are supported by the export path.

Sample payloads must be copied in track decode order. Every output sample offset must point inside the output `mdat`. Large files must use 64-bit chunk offsets when required. Unsupported container layouts or track features must fail explicitly rather than produce a potentially corrupt MP4.

## Validation requirements

Every export integration test should reopen the output using `Mp4FileReader` and verify:

- selected sample counts and sample descriptions;
- sample sizes, decode durations, composition offsets, and sync sample numbering;
- chunk offsets and payload bytes;
- track and movie durations;
- successful H.264 decode from the first output sync sample;
- audio/video timeline alignment where audio is present.

No export feature is complete until the output is independently validated by PixoraStudio's reader and decoders and the automated test suite passes.
