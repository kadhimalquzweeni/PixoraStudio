# H.264 CABAC validation plan

## Purpose

CABAC is implemented in the managed decoder. This document separates implementation coverage from evidence of end-to-end conformance so that the supported-feature list does not overstate what has been verified.

## Current synthetic regression coverage

| Layer | Existing coverage | Remaining gap |
|---|---|---|
| Arithmetic engine and context setup | Arithmetic decoding, context initialisation, bypass/terminate handling and context-state tests | Independent conformance vectors compared against a trusted CABAC implementation |
| Intra macroblock syntax/residuals | CABAC Intra4x4/Intra16x16 syntax, prediction-mode contexts, residual blocks and coded-block flags | Broader residual combinations and multi-macroblock/multi-slice fixtures |
| CABAC I-slice traversal | Intra4x4 reconstruction and I_PCM alignment/restart | Intra8x8, Intra16x16, non-zero residual and multi-slice picture traversal using real arithmetic-coded input |
| CABAC P-slice traversal | P_Skip reconstruction and reference/deblocking metadata | Coded P_L0 macroblock with non-zero residual; P intra/PCM CABAC paths; multi-macroblock context carry |
| CABAC B-slice traversal | B_Skip and a list-0 inter macroblock reconstruction | List-1 and bi-predicted coded macroblocks, direct modes, residual-bearing macroblocks and context carry |
| MP4 integration | MP4 end-to-end CAVLC I/P/B tests cover avcC, NAL extraction, DPB and POC output | CABAC samples have not yet been exercised end-to-end through avcC and MP4 track decoding |
| Real-stream conformance | No independently verified CABAC reference-output comparison recorded | Decode real CABAC streams and compare Y/Cb/Cr output against a trusted decoder |

The test files currently carrying the focused coverage include:

- `H264CabacArithmeticDecoderTests`
- `H264CabacContextInitializerTests`
- `H264CabacSyntaxElementReaderTests`
- `H264CabacResidualBlockReaderTests`
- `H264CabacIntraSliceDataDecoderTests`
- `H264PInterSliceDataDecoderTests`
- `H264BInterSliceDataDecoderTests`
- `H264Mp4AvcEndToEndTests` (currently CAVLC MP4 streams)

## Completion criteria

1. Add CABAC P-slice tests for a coded P_L0 macroblock, including a residual-bearing case and an intra/PCM case. Assert reconstructed pixels, QP, motion metadata, deblocking metadata, context-bin sequence and end-of-slice handling.
2. Extend B-slice CABAC tests to cover list-1/bi-prediction, direct prediction and residual-bearing macroblocks. Assert both reference lists and per-block motion/reference metadata.
3. Add multi-macroblock and multi-slice CABAC tests that prove picture-relative contexts are retained across slices and reset only at picture boundaries.
4. Add CABAC I/P/B MP4 access-unit tests using valid arithmetic-coded NAL units in length-prefixed MP4 samples. Assert complete surfaces, DPB state, sample timestamps and presentation ordering.
5. Decode a small set of real CABAC streams (at minimum I/P/B, 4:2:0, 8-bit, frame-coded) and compare the managed Y/Cb/Cr planes with an independently decoded reference. Record stream-generation commands, encoder settings, expected hashes/tolerances and any known exclusions.
6. Run the complete solution test suite and record the exact commit and command used. A passing synthetic suite alone is not a claim of full H.264 conformance.

## Local verification

Run from the repository root:

```powershell
dotnet test .\PixoraStudio.slnx --configuration Release
```

For focused iteration:

```powershell
dotnet test .\PixoraStudio.slnx --configuration Release --filter FullyQualifiedName~H264Cabac
dotnet test .\PixoraStudio.slnx --configuration Release --filter FullyQualifiedName~H264Mp4AvcEndToEnd
```

Do not mark the milestone complete until the completion criteria above are either met or explicitly split into a separately tracked follow-up with the remaining limitations stated.
