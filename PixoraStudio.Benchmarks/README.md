# H.264 decoder microbenchmarks

Run from the repository root:

```powershell
dotnet run -c Release --project PixoraStudio.Benchmarks -- --filter "*H264HotPathBenchmarks*"
```

The BenchmarkDotNet harness reports throughput and allocations for:
- fixed-width bit reads,
- unsigned Exp-Golomb decoding,
- CABAC context-coded decision bins,
- 4:2:0 chroma residual reconstruction.

Use the same .NET SDK, build configuration, and machine when comparing commits. Record the commit SHA and BenchmarkDotNet environment header with each result.

These are microbenchmarks, not end-to-end video decode measurements. In particular, the CABAC benchmark measures arithmetic decision-bin throughput with a fixed context path, while chroma reconstruction uses a deterministic zero-residual macroblock. Real-stream profiling is still required before drawing conclusions about total decoder performance.
