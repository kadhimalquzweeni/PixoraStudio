# Managed H.264/AVC decoding from MP4

PixoraStudio's managed AVC pipeline lives in `PixoraStudio.MediaEngine`. It reads AVC configuration and length-prefixed NAL units from an MP4 video track, reconstructs frame-coded pictures, maintains reference pictures, and emits frames in H.264 presentation order while preserving the MP4 sample timestamps.

## Decode a complete track

The MP4 parser supplies an `Mp4MediaInfo` object. Select its video track and pass the source stream to `H264Mp4TrackDecoder`:

```csharp
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.General;

H264Mp4TrackDecoder decoder = new();

IEnumerable<H264Mp4DecodedVideoFrame> frames = decoder.Decode(
    stream,
    mediaInfo,
    trackId,
    maxNumReorderFramesFallback: 2,
    cancellationToken);

foreach (H264Mp4DecodedVideoFrame frame in frames)
{
    H264DecodedPictureSurface surface = frame.Surface;

    // The surface contains decoded Y, Cb and Cr planes.
    // PresentationTimeTicks / DurationTicks use frame.Timescale.
    long pts = frame.PresentationTimeTicks;
    uint duration = frame.DurationTicks;
    uint timescale = frame.Timescale;
}
```

If the SPS VUI contains a valid bitstream restriction, its `max_num_reorder_frames` value is used. Otherwise the caller must provide a container-appropriate fallback. The fallback is not a frame-rate estimate; it is the maximum number of decoded pictures that may be held for presentation reordering.

The track decoder reads samples in decode order. Do not skip samples when using one decoder instance: reference-picture state depends on the preceding access units. Start from a random-access point (normally an IDR sample). Seeking is available through `DecodeFromTime`, which restarts from the preceding MP4 sync sample and decodes forward. Recovery from a corrupt access unit is not automatic; reset and seek again from a valid sync sample.

## Seek within an AVC track

Use `H264Mp4VideoSeekPlanner` to map a requested presentation time to the
latest preceding MP4 sync sample. `H264Mp4TrackDecoder.DecodeFromTime` then
starts at that random-access sample, decodes every following access unit in
decode order to rebuild reference-picture and POC state, and discards output
pictures whose presentation interval ends at or before the requested time:

```csharp
IEnumerable<H264Mp4DecodedVideoFrame> frames = new H264Mp4TrackDecoder().DecodeFromTime(
    stream,
    mediaInfo,
    trackId,
    targetPresentationTimeSeconds: 12.5,
    maxNumReorderFramesFallback: 2,
    cancellationToken);

foreach (H264Mp4DecodedVideoFrame frame in frames)
{
    // PresentationTimeTicks and DurationTicks remain in the source track clock.
    // Convert to seconds using frame.Timescale; do not use decode timestamps
    // as the display clock when B-frame reordering is present.
}
```

The seek planner uses MP4 composition offsets (PTS = DTS + composition offset),
clamps times beyond the presentation end to the final presentation tick, and
rejects tracks without a sync sample or with invalid sample timing. The returned
sequence starts with the frame whose presentation interval contains the target,
or the next frame when the target falls into a presentation gap. Seeking is
not equivalent to starting a fresh decoder at an arbitrary P/B sample: all
samples from the selected sync sample onward must be supplied contiguously.

## Decode individual samples

For callers that already read MP4 samples, use `H264Mp4TrackSampleReader` to resolve each sample's sample description, parse its `avcC` configuration, split its length-prefixed NAL units, and retain DTS, PTS and duration ticks. Create an `H264Mp4AccessUnitDecoder` from the first sample's parameter sets, then supply contiguous samples in decode order:

```csharp
H264Mp4TrackSampleReader sampleReader = new();
H264Mp4VideoSample first = sampleReader.Read(track, firstEncodedSample);

H264Mp4AccessUnitDecoder decoder =
    H264Mp4AccessUnitDecoder.CreateFromSample(
        first,
        maxNumReorderFramesFallback: 2);

H264Mp4AccessUnitDecodeResult? result = decoder.Decode(first);
foreach (H264Mp4DecodedVideoFrame frame in result?.ReadyFrames ?? [])
{
    // Consume frame.Surface and its MP4 timing properties.
}

// Continue with each subsequent encoded sample in decode order.
// Call Flush() at end-of-stream to emit any pictures still held for reordering.
IReadOnlyList<H264Mp4DecodedVideoFrame> finalFrames = decoder.Flush();
```

One MP4 sample represents one coded picture. Multiple VCL slices in that sample are assembled when they share picture identity and reference-list ordering. A failed picture decode faults the stateful decoder; call `Reset()` and restart from a valid random-access point rather than continuing with an incomplete reference chain.

## Supported bitstream subset

- Frame-coded, 8-bit AVC.
- CAVLC and CABAC entropy coding for the implemented frame-coded subset.
- 4:2:0 and 4:2:2 chroma.
- I, P and B slices, including the implemented intra/inter macroblock types, direct prediction, weighted prediction and CAVLC residual reconstruction.
- Short- and long-term reference management, POC types 0/1/2, frame output reordering and in-loop deblocking.
- MP4 `avc1` / `avc3` sample descriptions with a stable `avcC` parameter-set configuration. Dynamic in-band parameter-set updates are rejected.

## Explicit limitations

The decoder rejects unsupported syntax instead of silently approximating it. Current exclusions include FMO, field/MBAFF/interlaced pictures, separate colour planes, 4:4:4, high bit depth, SP/SI reconstruction and partitioned slice NAL units. CABAC has arithmetic/context/syntax unit coverage and focused I/P/B slice-level synthetic tests; the existing MP4 end-to-end suite still uses CAVLC streams, so CABAC-through-avcC/MP4 and independently decoded real-stream comparisons are not yet verified. Field pairing, streaming recovery and wiring the managed decoded frames into the WPF playback surface remain separate tasks. BGRA colour conversion and keyframe-based MP4 seeking are implemented in the media engine.

The supported subset still requires real-stream conformance validation against independently decoded reference output. Synthetic tests exercise parser, reconstruction, DPB, deblocking and MP4 integration paths, but do not establish complete H.264 conformance.
