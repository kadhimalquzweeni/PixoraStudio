# H.264 decoder microbenchmarks

Run from the repository root:

```powershell
dotnet run -c Release --project PixoraStudio.Benchmarks -- --filter "*H264HotPathBenchmarks*"
```

The BenchmarkDotNet harness reports throughput and allocations for:
- fixed-width bit reads,
- unsigned Exp-Golomb decoding,
- CABAC context-coded decision bins.

Use the same .NET SDK, build configuration, and machine when comparing commits. These are microbenchmarks, not end-to-end video decode measurements. Record the commit SHA and BenchmarkDotNet environment header with each result.
