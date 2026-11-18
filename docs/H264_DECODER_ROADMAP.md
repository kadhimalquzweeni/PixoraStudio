# H.264 / AVC decoder roadmap

PixoraStudio is implementing its own managed H.264 decoder. The existing MP4 reader already identifies AVC sample entries and retains the `avcC` configuration record; this work builds the elementary-stream decoder independently of the WPF playback layer.

## Milestone 18 – Bitstream foundation

- NAL unit framing for Annex B streams and MP4 length-prefixed samples.
- EBSP-to-RBSP conversion and emulation-prevention validation.
- MSB-first bit reading and unsigned/signed Exp-Golomb primitives.
- SPS and PPS syntax parsing and validation.
- No pixel reconstruction or playback integration is included in this milestone part.

## Subsequent decoder stages

1. Parse complete SPS/PPS syntax, including scaling matrices, picture-order-count configuration, slice groups and VUI/HRD metadata.
2. Parse slice headers and implement Baseline Profile CAVLC residual decoding.
3. Reconstruct intra-coded macroblocks and reference-frame management.
4. Add inter prediction, motion vectors and motion compensation.
5. Add CABAC and broader Main/High Profile support.
6. Convert decoded YUV pictures to display formats and integrate frame scheduling with MP4 timing and AAC playback.

Unsupported profiles, tools and malformed bitstreams must be rejected explicitly rather than silently producing corrupted frames.
