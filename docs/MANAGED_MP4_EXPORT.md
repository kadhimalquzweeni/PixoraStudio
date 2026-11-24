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

\`Mp4TrimExportPlanBuilder\` resolves requested times to actual video-frame presentation boundaries. It:

- validates finite, positive trim ranges within the movie duration;
- requires a video track and a usable sync sample;
- maps requested start/end times to the first video frame whose presentation timestamp is at or after each requested boundary;
- uses presentation timestamps rather than decode order, so B-frame reordering does not shift the visible trim;
- retains the preceding sync sample and the complete decode-order GOP needed for reference-picture reconstruction;
- writes an edit list whose media time hides decode-preroll frames and whose duration ends on the selected frame boundary;
- selects audio packets overlapping the visible video interval, then applies the same movie-time edit window;
- preserves contiguous decode-order sample selections and rejects discontinuous audio edit-list selections.

This is frame-accurate at the encoded video frame level without re-encoding. The encoded sample payloads are copied unchanged; the output MP4 edit list controls which decoded frames are presented. The visible range can differ from the raw mouse position by less than one frame because trim boundaries are resolved to actual frame timestamps. Keyframe preroll remains in the file but is not displayed.

The current planner explicitly rejects source video/audio edit lists. Supporting those correctly requires translating the source presentation timeline into new output edit lists and is deferred until the track/movie metadata stage.

The managed implementation now includes:

- `Mp4BoxWriter` writes normal and extended-size ISO BMFF boxes.
- `Mp4BoxTreeWriter` rebuilds parsed containers while preserving unchanged leaf boxes byte-for-byte and supports full-box replacement.
- `Mp4SampleTableBoxWriter` writes sample descriptions, sample timing, chunk mapping, sizes, sync samples, composition offsets and 32-/64-bit chunk offsets.
- `Mp4MediaDataWriter` streams selected encoded sample bytes into a new extended-size `mdat` and returns their absolute output offsets.
- `Mp4DurationBoxWriter` updates movie, track and media duration fields without changing other header fields.
- `Mp4EditListBoxWriter` and `Mp4TrimMoovWriter` rebuild output edit lists, track headers, media headers and the `moov` box.
- `Mp4TrimExportFileWriter` assembles the complete `ftyp + moov + mdat` output, resolves offsets in a two-pass layout, reopens the result with PixoraStudio's MP4 reader and validates track/sample counts and duration.
- `VideoExportService` writes to a temporary file and replaces the destination only after export validation succeeds.
- The video editor exposes export, progress and cancellation controls.

The end-to-end test creates a synthetic MP4, remuxes a selected GOP range, reopens the output and verifies that the encoded sample bytes are unchanged.

## Container writer requirements

The writer rebuilds, rather than blindly copying, all timing and sample-location metadata affected by trimming:

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

The current implementation is a frame-accurate MP4 remuxer, not a re-encoder. It preserves the original compressed samples and uses edit lists to clip presentation at exact video-frame boundaries. It currently accepts MP4 source files only and rejects fragmented MP4, source edit lists, multiple video tracks, non-audio/video tracks, unsupported top-level boxes and unsupported sample-table extensions. These cases must fail explicitly rather than produce an output that may lose media or metadata.

The output is reopened and validated by PixoraStudio's reader. The H.264 decoder should also be exercised against exported real-world samples before describing the exporter as broadly production-ready.
