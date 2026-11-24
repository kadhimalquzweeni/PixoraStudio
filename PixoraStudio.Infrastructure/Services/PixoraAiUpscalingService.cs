using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PixoraStudio.Core.Interfaces;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace PixoraStudio.Infrastructure.Services;

public sealed class PixoraAiUpscalingService : IAiUpscalingService, IDisposable
{
    private const int Scale = 4;
    private const int CoreTileSize = 128;
    private const int TileHalo = 24;
    private const long MaximumOutputPixels = 32_000_000;
    private readonly object _sessionLock = new();
    private readonly object _runLock = new();
    private readonly Dictionary<AiUpscaleModel, InferenceSession> _sessions = [];
    private readonly HashSet<AiUpscaleModel> _gpuModels = [];
    private bool _disposed;

    private static string ModelPath(AiUpscaleModel model) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PixoraStudio", "Models",
        model switch
        {
            AiUpscaleModel.Photo => "pixora_sr_x4.onnx",
            AiUpscaleModel.Sprite => "pixora_sr_sprite_x4.onnx",
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        });

    public bool IsModelAvailable(AiUpscaleModel model) => File.Exists(ModelPath(model));

    public Task<SKBitmap> UpscaleFourTimesAsync(
        SKBitmap source,
        AiUpscaleModel model,
        IProgress<AiUpscaleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Task.Run(() => Upscale(source, model, progress, cancellationToken), cancellationToken);
    }

    private SKBitmap Upscale(
        SKBitmap source,
        AiUpscaleModel model,
        IProgress<AiUpscaleProgress>? progress,
        CancellationToken cancellationToken)
    {
        string modelPath = ModelPath(model);
        if (!IsModelAvailable(model))
            throw new FileNotFoundException(
                "Train Pixora's local upscaler first. The selected model file was not found.", modelPath);

        int outputWidth = checked(source.Width * Scale);
        int outputHeight = checked(source.Height * Scale);
        if (source.Width <= 0 || source.Height <= 0 ||
            (long)outputWidth * outputHeight > MaximumOutputPixels)
            throw new ArgumentOutOfRangeException(nameof(source),
                "Pixora AI supports 4x results up to 32 megapixels. Choose a smaller image or use regular resizing.");

        (InferenceSession session, bool usingGpu) = GetSession(model);
        int columns = (source.Width + CoreTileSize - 1) / CoreTileSize;
        int rows = (source.Height + CoreTileSize - 1) / CoreTileSize;
        int totalTiles = checked(columns * rows);
        int completedTiles = 0;
        var imageInfo = new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        var result = new SKBitmap(imageInfo);
        if (result.GetPixels() == IntPtr.Zero)
        {
            result.Dispose();
            throw new OutOfMemoryException("Could not allocate the Pixora AI output image.");
        }

        try
        {
        int rowBytes = result.RowBytes;
        byte[] pixelBytes = new byte[checked(rowBytes * outputHeight)];
        byte[] sourceAlpha = new byte[checked(source.Width * source.Height)];
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            sourceAlpha[y * source.Width + x] = source.GetPixel(x, y).Alpha;
        progress?.Report(new AiUpscaleProgress(
            usingGpu ? "Pixora AI is running on the GPU" : "Pixora AI is running on the CPU", 0));

        for (int coreTop = 0; coreTop < source.Height; coreTop += CoreTileSize)
        for (int coreLeft = 0; coreLeft < source.Width; coreLeft += CoreTileSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int coreRight = Math.Min(source.Width, coreLeft + CoreTileSize);
            int coreBottom = Math.Min(source.Height, coreTop + CoreTileSize);
            int inputLeft = Math.Max(0, coreLeft - TileHalo);
            int inputTop = Math.Max(0, coreTop - TileHalo);
            int inputRight = Math.Min(source.Width, coreRight + TileHalo);
            int inputBottom = Math.Min(source.Height, coreBottom + TileHalo);
            int inputWidth = inputRight - inputLeft;
            int inputHeight = inputBottom - inputTop;

            float[] inputData = new float[checked(3 * inputWidth * inputHeight)];
            for (int y = 0; y < inputHeight; y++)
            for (int x = 0; x < inputWidth; x++)
            {
                SKColor color = source.GetPixel(inputLeft + x, inputTop + y);
                int position = y * inputWidth + x;
                inputData[position] = color.Red / 255f;
                inputData[inputWidth * inputHeight + position] = color.Green / 255f;
                inputData[2 * inputWidth * inputHeight + position] = color.Blue / 255f;
            }

            var inputTensor = new DenseTensor<float>(inputData, [1, 3, inputHeight, inputWidth]);
            var inputValue = NamedOnnxValue.CreateFromTensor("input", inputTensor);
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputValues;
            lock (_runLock)
                outputValues = session.Run([inputValue]);

            using (outputValues)
            {
                Tensor<float> outputTensor = outputValues.First().AsTensor<float>();
                int tileHeight = outputTensor.Dimensions[2];
                int tileWidth = outputTensor.Dimensions[3];
                float[] outputData = outputTensor.ToArray();
                int planeSize = checked(tileHeight * tileWidth);
                if (tileWidth != inputWidth * Scale || tileHeight != inputHeight * Scale)
                    throw new InvalidOperationException("The Pixora model returned an unexpected output size.");

                int destinationLeft = coreLeft * Scale;
                int destinationTop = coreTop * Scale;
                int destinationRight = coreRight * Scale;
                int destinationBottom = coreBottom * Scale;
                int tileSourceLeft = (coreLeft - inputLeft) * Scale;
                int tileSourceTop = (coreTop - inputTop) * Scale;
                for (int y = destinationTop; y < destinationBottom; y++)
                for (int x = destinationLeft; x < destinationRight; x++)
                {
                    int tileX = tileSourceLeft + x - destinationLeft;
                    int tileY = tileSourceTop + y - destinationTop;
                    int tilePosition = tileY * tileWidth + tileX;
                    float sourceX = ((x + 0.5f) / Scale) - 0.5f;
                    float sourceY = ((y + 0.5f) / Scale) - 0.5f;
                    byte alpha = SampleAlpha(sourceAlpha, source.Width, source.Height, sourceX, sourceY);
                    int pixelOffset = y * rowBytes + x * 4;
                    pixelBytes[pixelOffset] = Premultiply(ToByte(outputData[tilePosition]), alpha);
                    pixelBytes[pixelOffset + 1] =
                        Premultiply(ToByte(outputData[planeSize + tilePosition]), alpha);
                    pixelBytes[pixelOffset + 2] =
                        Premultiply(ToByte(outputData[planeSize * 2 + tilePosition]), alpha);
                    pixelBytes[pixelOffset + 3] = alpha;
                }
            }

            completedTiles++;
            progress?.Report(new AiUpscaleProgress(
                string.Format("Enhancing tile {0} of {1}", completedTiles, totalTiles),
                100d * completedTiles / totalTiles));
        }

        Marshal.Copy(pixelBytes, 0, result.GetPixels(), pixelBytes.Length);
        return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private (InferenceSession Session, bool UsingGpu) GetSession(AiUpscaleModel model)
    {
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.TryGetValue(model, out InferenceSession? existing))
                return (existing, _gpuModels.Contains(model));

            try
            {
                using var options = new SessionOptions
                {
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    EnableMemoryPattern = false,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };
                options.AppendExecutionProvider_DML(0);
                var session = new InferenceSession(ModelPath(model), options);
                _sessions.Add(model, session);
                _gpuModels.Add(model);
                return (session, true);
            }
            catch (Exception)
            {
                var session = new InferenceSession(ModelPath(model));
                _sessions.Add(model, session);
                return (session, false);
            }
        }
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)Math.Round(value * 255f), 0, 255);

    private static byte Premultiply(byte color, byte alpha) =>
        (byte)((color * alpha + 127) / 255);

    private static byte SampleAlpha(byte[] alpha, int width, int height, float x, float y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        int left = (int)x;
        int top = (int)y;
        int right = Math.Min(left + 1, width - 1);
        int bottom = Math.Min(top + 1, height - 1);
        float fx = x - left;
        float fy = y - top;
        float topAlpha = alpha[top * width + left] * (1 - fx) +
                         alpha[top * width + right] * fx;
        float bottomAlpha = alpha[bottom * width + left] * (1 - fx) +
                            alpha[bottom * width + right] * fx;
        return (byte)Math.Clamp((int)Math.Round(topAlpha * (1 - fy) + bottomAlpha * fy), 0, 255);
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (InferenceSession session in _sessions.Values)
                session.Dispose();
            _sessions.Clear();
            _gpuModels.Clear();
        }
    }
}
