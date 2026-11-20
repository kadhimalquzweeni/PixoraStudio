# H.264 decoder microbenchmarks

Run from the repository root:

```powershell
dotnet run -c Release --project PixoraStudio.Benchmarks -- --filter "*H264HotPathBenchmarks*"
```

The BenchmarkDotNet harness reports throughput and allocations for:
- fixed-width bit reads,
- unsigned Exp-Golomb decoding,
- CABAC context-coded decision bins,
- integer and fractional motion compensation,
- inverse 8x8 transforms,
- 4:2:0 chroma residual reconstruction.

Use the same .NET SDK, build configuration, and machine when comparing commits. Record the commit SHA and BenchmarkDotNet environment header with each result.

## Thumbnail and slice parallelism

The managed MP4 thumbnail pipeline has two independent levels of parallelism:

| Environment variable | Purpose | Behaviour |
| --- | --- | --- |
| `PIXORA_H264_THUMBNAIL_WORKERS` | Concurrent thumbnail seek/decode jobs | Defaults to up to 4 workers, capped by processor count and requested thumbnail count |
| `PIXORA_H264_SLICE_WORKERS` | Concurrent slices within one access unit | Defaults to 1–2 workers based on processor count; an explicit value is clamped to 1–4 |
| `PIXORA_H264_CABAC_PROFILE` | CABAC diagnostic profiling | Set to `1` to collect decision/bypass/renormalization counters |

Multi-slice I, P and B pictures can decode concurrently. Single-slice pictures retain the normal single-slice path. P/B workers keep motion-vector and entropy/neighbour state local to each slice, then merge motion metadata after the workers complete.

For a meaningful throughput comparison, keep the video, thumbnail positions, build mode and machine fixed:
1. Compare thumbnail workers 1, 2 and 4 with slice workers set to 1.
2. Keep thumbnail workers fixed and compare slice workers 1, 2 and 4 on a video that actually contains multi-slice pictures.
3. Run CABAC profiling separately. Profiling adds overhead, so do not compare profiled throughput with unprofiled runs.

These are microbenchmarks, not end-to-end video decode measurements. In particular, the CABAC benchmark measures arithmetic decision-bin throughput with a fixed context path, while chroma reconstruction uses a deterministic zero-residual macroblock. Real-stream profiling and elapsed-time measurements are still required before drawing conclusions about total decoder performance.
