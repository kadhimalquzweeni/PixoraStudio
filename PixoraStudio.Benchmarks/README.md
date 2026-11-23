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

## Thumbnail worker configuration

The managed MP4 thumbnail pipeline can decode multiple requested thumbnail positions concurrently. Configure this with:

| Environment variable | Purpose | Behaviour |
| --- | --- | --- |
| `PIXORA_H264_THUMBNAIL_WORKERS` | Concurrent thumbnail seek/decode jobs | Defaults to up to 4 workers, capped by processor count and requested thumbnail count |
| `PIXORA_H264_CABAC_PROFILE` | CABAC diagnostic profiling | Set to `1` to collect decision/bypass/renormalization counters |

Slices within an individual MP4 access unit are decoded sequentially in bitstream order. Thumbnail jobs remain independently parallel, so multiple thumbnails can still be processed concurrently.

For a meaningful thumbnail throughput comparison, keep the video, thumbnail positions, build mode and machine fixed. Compare thumbnail worker counts of 1, 2 and 4, and record the commit SHA and elapsed time. Run CABAC profiling separately because profiling adds overhead.

## Photo editor GPU preview comparison

The photo editor uses SkiaSharp's OpenGL-backed WPF surface for live layer compositing and adjustment shaders. Image export remains on the CPU path for deterministic output.

To compare the GPU preview with the CPU fallback, run the same document and editing sequence in two separate app launches:

- **GPU preview:** launch normally.
- **CPU fallback:** set `PIXORA_DISABLE_GPU_PREVIEW=1` before launching PixoraStudio.

PowerShell example for the CPU fallback:

```powershell
$env:PIXORA_DISABLE_GPU_PREVIEW = "1"
dotnet run --project PixoraStudio.App
```

Use the same source image, canvas dimensions, layer count, visibility settings and adjustment sequence in both runs. Record preview responsiveness, CPU utilization, GPU utilization and memory use. Repeat with a large image and several layers. The CI runner validates shader calculations and rendering behaviour on a raster surface; it does not provide a reliable physical-GPU performance measurement, so hardware benchmarks must be run on the target Windows machine.

The GPU surface requires a working OpenGL driver and OpenGL/DirectX interop. If initialization or shader rendering fails, PixoraStudio automatically returns to the CPU preview.
