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
- VUI/HRD parsing is covered by focused synthetic unit tests.
- PPS parsing now covers reference defaults, weighted prediction, QP offsets, deblocking/constrained-intra/redundant-picture flags, all FMO map syntax types (0–6), and the optional transform/scaling-matrix extension. The SPS is accepted as an optional context when PPS scaling-list count depends on chroma format.
- Slice-header parsing is implemented for ordinary non-IDR and IDR slice NAL units, covering I/P/B/SP/SI slice types, frame/field syntax, POC types 0/1/2, reference-count overrides, reference-list modifications, weighted prediction tables, decoded-reference marking/MMCO, CABAC initialisation syntax, QP/deblocking syntax and FMO slice-group change cycles.
- A stateful POC calculator now derives top-field, bottom-field and picture order counts for POC types 0, 1 and 2, including LSB wraparound, frame-number wraparound, reference/non-reference pictures, field pictures, IDR resets and MMCO 5 state resets. It must be called once per primary coded picture.
- SPS now retains the frame-number gap flag, MBAFF flag and POC type 1 offset-cycle values needed by picture-order derivation.
- Focused synthetic slice-header tests cover IDR/I, P with list modification and MMCO, B weighted prediction, POC type 1 field pictures, SP/CABAC and FMO cycle parsing.
- CAVLC VLC tables are implemented for context-selected coeff_token, separate 4:2:0 and 4:2:2 chroma-DC coeff_token, total_zeros and run_before. Residual-block decoding assembles trailing-one signs, coefficient levels (including level-prefix escape handling), total_zeros and run_before into forward scan-order coefficient arrays. 4x4 zigzag/field and 8x8 zigzag/field scan conversion are implemented. The 4x4 and 8x8 inverse integer transforms and 2x2 chroma-DC Hadamard transform are implemented with focused tests. A 4x4 inverse quantiser now applies the three H.264 position classes, QP quotient/remainder scaling and raster-order scaling lists; slice luma QP derivation includes the SPS bit-depth offset and range checks. SPS/PPS scaling-list syntax is retained, including use-default markers, and the scaling-matrix resolver applies normative defaults, list inheritance, SPS-to-PPS fallback and raster-order conversion for both 4x4 and 8x8 matrices. The 8x8 resolver supports the two lists used by 4:2:0/4:2:2 and all six lists for 4:4:4. The 8x8 inverse quantiser applies the six normative coefficient position classes, QP quotient/remainder scaling and resolved raster-order matrices. Both quantisers can consume effective SPS/PPS matrices directly. Macroblock reconstruction remains outstanding.
- Milestone 19 work has started with a slice-aware CAVLC neighbour-context state. It tracks decoded 4x4 block TotalCoeff values, derives nC from available left/top neighbours using the normative rounded average, and treats picture edges, undecoded neighbours and neighbours in another slice as unavailable. The context wrapper decodes ordinary 15/16-coefficient blocks and records their TotalCoeff; chroma DC blocks continue to use their dedicated VLC context. Focused tests cover neighbour availability, averaging, slice boundaries, zero-coefficient neighbours and residual-reader integration. Full macroblock syntax and slice-data traversal are not yet implemented.
- CABAC, pixel reconstruction, motion compensation, decoded-picture-buffer management, reference-list construction, POC-based output reordering and playback integration remain outstanding.

The roadmap is complete only when the implemented feature set is backed by automated tests and real H.264 streams with independently verified decoded output.
