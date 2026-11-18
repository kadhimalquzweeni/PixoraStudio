# H.264 / AVC decoder roadmap

PixoraStudio is implementing a managed H.264/AVC decoder in `PixoraStudio.MediaEngine`. The target is native decoding without FFmpeg or LibVLC in the decoding path. Those tools may be used externally to create reference test streams, but are not runtime decoder dependencies.

## Decoder scope and support policy

The roadmap targets a standards-based decoder with explicit profile/tool capability checks. H.264 is not a single fixed bitstream format: profiles, chroma formats, bit depths, entropy modes, interlace modes and optional tools vary substantially. A stream must be rejected with a clear unsupported-feature error whenever its syntax or decoding tools are not implemented. It must never be silently rendered as if correctly decoded.

## Implementation stages

1. **Bitstream foundation** — Annex B and MP4 length-prefixed NAL framing, EBSP/RBSP conversion, bit reading and Exp-Golomb primitives. Initial SPS/PPS parsing is in place.
2. **Parameter sets and picture syntax** — complete SPS/PPS including VUI/HRD, slice-group syntax, slice headers, picture-order-count derivation and reference-list modification.
3. **Baseline profile reconstruction** — CAVLC residual parsing, inverse scans/transforms/dequantisation, intra prediction, macroblock reconstruction and decoded-picture-buffer management.
4. **Inter prediction** — P/B slice syntax, reference picture lists, motion-vector prediction, sub-pixel interpolation, weighted prediction and motion compensation.
5. **Additional profiles/tools** — CABAC, Main/High profile transforms and prediction tools, field coding and supported chroma/bit-depth variants. Each tool is gated by conformance tests.
6. **Output and container integration** — cropped YUV picture surfaces, colour conversion, MP4 sample/decode-order scheduling, POC reordering, seeking and synchronisation with the existing AAC playback path.
7. **Compatibility and hardening** — real-stream test corpus, malformed/truncated bitstreams, resource limits, cancellation and regression/performance tests.

## Current implementation status

- NAL framing, EBSP-to-RBSP conversion, bit reader and initial SPS/PPS readers are implemented.
- SPS parsing now reads VUI aspect ratio, overscan, video signal/colour description, chroma location, timing, NAL/VCL HRD, picture-structure and bitstream-restriction metadata.
- VUI/HRD parsing has focused synthetic unit tests. The test suite still needs to be run locally after these changes.
- PPS parsing now covers reference defaults, weighted prediction, QP offsets, deblocking/constrained-intra/redundant-picture flags, all FMO map syntax types (0–6), and the optional transform/scaling-matrix extension. The SPS is accepted as an optional context when PPS scaling-list count depends on chroma format.
- Slice-header parsing is now implemented for ordinary non-IDR and IDR slice NAL units, covering I/P/B/SP/SI slice types, frame/field syntax, POC types 0/1/2, reference-count overrides, reference-list modifications, weighted prediction tables, decoded-reference marking/MMCO, CABAC initialisation syntax, QP/deblocking syntax and FMO slice-group change cycles.
- SPS now retains the frame-number gap flag, MBAFF flag and POC type 1 offset-cycle values needed by picture-order derivation.
- Focused synthetic slice-header tests cover IDR/I, P with list modification and MMCO, B weighted prediction, POC type 1 field pictures, SP/CABAC and FMO cycle parsing. The full local test suite must be run before this stage is considered verified.
- Entropy decoding (CAVLC/CABAC), pixel reconstruction, motion compensation, decoded-picture-buffer management, POC derivation/reordering and playback integration remain outstanding.

The roadmap is complete only when the implemented feature set is backed by automated tests and real H.264 streams with independently verified decoded output.
