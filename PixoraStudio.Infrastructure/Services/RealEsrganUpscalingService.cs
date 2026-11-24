using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PixoraStudio.Core.Interfaces;
using SkiaSharp;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Runs the Real-ESRGAN x4plus ONNX model locally. The model is downloaded on
/// first use and verified against its pinned SHA-256 before it is loaded.
/// Model source: anakhiu/realesrgan-onnx, BSD-3-Clause, derived from xinntao/Real-ESRGAN.
/// </summary>
public sealed class RealEsrganUpscalingService : IAiUpscalingService, IDisposable
{
    private const int TileCoreSize = 128;
    private const int TileHalo = 16;
    private const string ModelSha256 = "1C0FC21D1D6D7DF7F39C1FA23C73138A87A01EA0B3AC56EBD0EA943E62E12800";
    private const string ModelUrl =
        "https://huggingface.co/anakhiu/realesrgan-onnx/resolve/b699e714820b6b277cfc92be0c3c8b522d47bc9b/realesrgan_x4plus.onnx";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private readonly object _sessionLock = new();
    private InferenceSession? _session;
    private bool _disposed;

    public async Task<SKBitmap> UpscaleFourTimesAsync(
        SKBitmap source,
        IProgress<AiUpscaleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);

        string modelPath = await EnsureModelAsync(progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(new AiUpscaleProgress("Loading the local AI model…", 22));

        var session = await Task.Run(() => GetOrCreateSession(modelPath), cancellationToken)
            .ConfigureAwait(false);
        return await Task.Run(
            () => UpscaleCore(source, session, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateSource(SKBitmap source)
    {
        long outputPixels = checked((long)source.Width * source.Height *
                                    AiUpscalingLimits.Scale * AiUpscalingLimits.Scale);
        if (source.Width <= 0 || source.Height <= 0 ||
            source.Width > AiUpscalingLimits.MaximumInputDimension ||
            source.Height > AiUpscalingLimits.MaximumInputDimension ||
            outputPixels > AiUpscalingLimits.MaximumOutputPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                "AI 4× upscaling is limited to images that produce at most 48 megapixels.");
        }
    }

    private async Task<string> EnsureModelAsync(
        IProgress<AiUpscaleProgress>? progress,
        CancellationToken cancellationToken)
    {
        string modelDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PixoraStudio", "Models", "RealESRGAN");
        string modelPath = Path.Combine(modelDirectory, "realesrgan_x4plus.onnx");
        string temporaryPath = modelPath + ".download";

        await _modelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(modelDirectory);
            if (File.Exists(modelPath) && await IsValidModelAsync(modelPath, cancellationToken).ConfigureAwait(false))
                return modelPath;

            if (File.Exists(modelPath))
                File.Delete(modelPath);

            progress?.Report(new AiUpscaleProgress("Downloading the AI model (about 67 MB)…", 0));
            try
            {
                using var response = await HttpClient.GetAsync(
                    ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                long contentLength = response.Content.Headers.ContentLength ?? 0;

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                                 .ConfigureAwait(false))
                await using (var output = new FileStream(
                                 temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                 1024 * 1024, useAsync: true))
                {
                    var buffer = new byte[1024 * 1024];
                    long totalBytes = 0;
                    while (true)
                    {
                        int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                            break;

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            .ConfigureAwait(false);
                        totalBytes += read;
                        if (contentLength > 0)
                        {
                            double percentage = Math.Clamp(totalBytes / (double)contentLength, 0d, 1d);
                            progress?.Report(new AiUpscaleProgress(
                                "Downloading the AI model (about 67 MB)…", percentage * 20d));
                        }
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!await IsValidModelAsync(temporaryPath, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("The downloaded AI model failed its integrity check.");

                File.Move(temporaryPath, modelPath, overwrite: true);
                progress?.Report(new AiUpscaleProgress("AI model ready.", 20));
                return modelPath;
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }
        finally
        {
            _modelLock.Release();
        }
    }

    private static async Task<bool> IsValidModelAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(ModelSha256, StringComparison.OrdinalIgnoreCase);
    }

    private InferenceSession GetOrCreateSession(string modelPath)
    {
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null)
                return _session;

            try
            {
                using var directMlOptions = CreateSessionOptions();
                directMlOptions.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(modelPath, directMlOptions);
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or
                                       EntryPointNotFoundException or InvalidOperationException)
            {
                using var cpuOptions = CreateSessionOptions();
                _session = new InferenceSession(modelPath, cpuOptions);
            }

            return _session;
        }
    }

    private static SessionOptions CreateSessionOptions() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        EnableMemoryPattern = false
    };

    private static SKBitmap UpscaleCore(
        SKBitmap source,
        InferenceSession session,
        IProgress<AiUpscaleProgress>? progress,
        CancellationToken cancellationToken)
    {
        int outputWidth = checked(source.Width * AiUpscalingLimits.Scale);
        int outputHeight = checked(source.Height * AiUpscalingLimits.Scale);
        byte[] sourceAlpha = ReadAlpha(source);
        var result = new SKBitmap(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        try
        {
            IntPtr resultPixels = result.GetPixels();
            if (resultPixels == IntPtr.Zero)
                throw new OutOfMemoryException("Unable to allocate the AI upscaled image.");

            string inputName = session.InputMetadata.Keys.First();
            string outputName = session.OutputMetadata.Keys.First();
            int tilesAcross = (source.Width + TileCoreSize - 1) / TileCoreSize;
            int tilesDown = (source.Height + TileCoreSize - 1) / TileCoreSize;
            int totalTiles = checked(tilesAcross * tilesDown);
            int completedTiles = 0;

            for (int coreTop = 0; coreTop < source.Height; coreTop += TileCoreSize)
            for (int coreLeft = 0; coreLeft < source.Width; coreLeft += TileCoreSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int coreWidth = Math.Min(TileCoreSize, source.Width - coreLeft);
                int coreHeight = Math.Min(TileCoreSize, source.Height - coreTop);
                int tileLeft = Math.Max(0, coreLeft - TileHalo);
                int tileTop = Math.Max(0, coreTop - TileHalo);
                int tileRight = Math.Min(source.Width, coreLeft + coreWidth + TileHalo);
                int tileBottom = Math.Min(source.Height, coreTop + coreHeight + TileHalo);
                int tileWidth = tileRight - tileLeft;
                int tileHeight = tileBottom - tileTop;
                var inputTensor = new DenseTensor<float>(new[] { 1, 3, tileHeight, tileWidth });

                for (int y = 0; y < tileHeight; y++)
                for (int x = 0; x < tileWidth; x++)
                {
                    var color = source.GetPixel(tileLeft + x, tileTop + y);
                    inputTensor[0, 0, y, x] = color.Red / 255f;
                    inputTensor[0, 1, y, x] = color.Green / 255f;
                    inputTensor[0, 2, y, x] = color.Blue / 255f;
                }

                var input = NamedOnnxValue.CreateFromTensor(inputName, inputTensor);
                using var outputs = session.Run(new[] { input }, new[] { outputName });
                var outputTensor = outputs.First().AsTensor<float>();

                int cropX = (coreLeft - tileLeft) * AiUpscalingLimits.Scale;
                int cropY = (coreTop - tileTop) * AiUpscalingLimits.Scale;
                int copyWidth = coreWidth * AiUpscalingLimits.Scale;
                int copyHeight = coreHeight * AiUpscalingLimits.Scale;
                var outputRow = new byte[copyWidth * 4];

                for (int y = 0; y < copyHeight; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int outputY = coreTop * AiUpscalingLimits.Scale + y;
                    for (int x = 0; x < copyWidth; x++)
                    {
                        int outputX = coreLeft * AiUpscalingLimits.Scale + x;
                        float alphaValue = SampleAlpha(
                            sourceAlpha, source.Width, source.Height, outputX, outputY);
                        byte alpha = ToByte(alphaValue);
                        int tileOutputX = cropX + x;
                        int tileOutputY = cropY + y;
                        byte red = ToByte(outputTensor[0, 0, tileOutputY, tileOutputX]);
                        byte green = ToByte(outputTensor[0, 1, tileOutputY, tileOutputX]);
                        byte blue = ToByte(outputTensor[0, 2, tileOutputY, tileOutputX]);
                        int pixel = x * 4;
                        outputRow[pixel] = Premultiply(red, alpha);
                        outputRow[pixel + 1] = Premultiply(green, alpha);
                        outputRow[pixel + 2] = Premultiply(blue, alpha);
                        outputRow[pixel + 3] = alpha;
                    }

                    IntPtr destination = IntPtr.Add(
                        resultPixels,
                        checked(outputY * result.RowBytes + coreLeft * AiUpscalingLimits.Scale * 4));
                    Marshal.Copy(outputRow, 0, destination, outputRow.Length);
                }

                completedTiles++;
                double percentage = 20d + 80d * completedTiles / totalTiles;
                progress?.Report(new AiUpscaleProgress(
                    $"AI upscaling image… {completedTiles} of {totalTiles} tiles",
                    percentage));
            }

            return result;
        }
        catch (OnnxRuntimeException ex)
        {
            result.Dispose();
            throw new InvalidOperationException($"The AI upscaler failed: {ex.Message}", ex);
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static byte[] ReadAlpha(SKBitmap bitmap)
    {
        var alpha = new byte[checked(bitmap.Width * bitmap.Height)];
        if (bitmap.AlphaType == SKAlphaType.Opaque)
        {
            Array.Fill(alpha, byte.MaxValue);
            return alpha;
        }

        if (bitmap.ColorType == SKColorType.Rgba8888)
        {
            IntPtr pixels = bitmap.GetPixels();
            int rowBytes = bitmap.RowBytes;
            var row = new byte[rowBytes];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(pixels, checked(y * rowBytes)), row, 0, rowBytes);
                for (int x = 0; x < bitmap.Width; x++)
                    alpha[y * bitmap.Width + x] = row[x * 4 + 3];
            }

            return alpha;
        }

        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            alpha[y * bitmap.Width + x] = bitmap.GetPixel(x, y).Alpha;

        return alpha;
    }

    private static float SampleAlpha(
        byte[] alpha, int sourceWidth, int sourceHeight, int outputX, int outputY)
    {
        double sourceX = Math.Clamp(
            (outputX + 0.5d) / AiUpscalingLimits.Scale - 0.5d, 0d, sourceWidth - 1d);
        double sourceY = Math.Clamp(
            (outputY + 0.5d) / AiUpscalingLimits.Scale - 0.5d, 0d, sourceHeight - 1d);
        int left = (int)Math.Floor(sourceX);
        int top = (int)Math.Floor(sourceY);
        int right = Math.Min(sourceWidth - 1, left + 1);
        int bottom = Math.Min(sourceHeight - 1, top + 1);
        double horizontal = sourceX - left;
        double vertical = sourceY - top;
        double topAlpha = alpha[top * sourceWidth + left] * (1d - horizontal) +
                          alpha[top * sourceWidth + right] * horizontal;
        double bottomAlpha = alpha[bottom * sourceWidth + left] * (1d - horizontal) +
                             alpha[bottom * sourceWidth + right] * horizontal;
        return (float)((topAlpha * (1d - vertical) + bottomAlpha * vertical) / 255d);
    }

    private static byte ToByte(float value)
    {
        if (!float.IsFinite(value))
            return 0;
        return (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
    }

    private static byte Premultiply(byte color, byte alpha) =>
        (byte)((color * alpha + 127) / 255);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }

        _modelLock.Dispose();
    }
}
