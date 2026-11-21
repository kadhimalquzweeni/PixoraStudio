using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Extracts video frames through Media Foundation's Source Reader with a D3D11
/// device manager attached. This enables DXVA-capable H.264 decoder transforms.
/// Pixel readback and PNG encoding are performed after decoding.
/// </summary>
public sealed class MediaFoundationVideoThumbnailDecoder : IHardwareVideoThumbnailDecoder
{
    private const int ThumbnailWidth = 240;
    private const long MediaFoundationTicksPerSecond = 10_000_000;
    private const uint CoInitializeMultithreaded = 0;

    public Task<bool> TryGenerateThumbnailsAsync(
        string filePath,
        IReadOnlyList<TimeSpan> positions,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(outputPaths);

        if (!OperatingSystem.IsWindows())
            return Task.FromResult(false);

        return Task.Run(
            () => TryGenerateCore(filePath, positions, outputPaths, progress, cancellationToken),
            cancellationToken);
    }

    private static bool TryGenerateCore(
        string filePath,
        IReadOnlyList<TimeSpan> positions,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress,
        CancellationToken cancellationToken)
    {
        int comResult = CoInitializeEx(IntPtr.Zero, CoInitializeMultithreaded);
        bool comInitialized = comResult >= 0;
        bool mediaFoundationStarted = false;

        try
        {
            Marshal.ThrowExceptionForHR(comResult);
            cancellationToken.ThrowIfCancellationRequested();

            MediaFactory.MFStartup().CheckError();
            mediaFoundationStarted = true;

            using ID3D11Device d3dDevice = D3D11.D3D11CreateDevice(
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0);

            using IMFDXGIDeviceManager deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
            deviceManager.ResetDevice(d3dDevice).CheckError();

            using IMFAttributes attributes = MediaFactory.MFCreateAttributes(3);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1).CheckError();
            attributes.Set(SourceReaderAttributeKeys.D3DManager, deviceManager).CheckError();
            attributes.Set(SourceReaderAttributeKeys.DisableDxva, 0).CheckError();
            attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, true).CheckError();

            using IMFSourceReader reader =
                MediaFactory.MFCreateSourceReaderFromURL(Path.GetFullPath(filePath), attributes);

            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

            using IMFMediaType nativeType =
                reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);

            ulong nativeSize = nativeType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            int sourceWidth = checked((int)(nativeSize >> 32));
            int sourceHeight = checked((int)(nativeSize & uint.MaxValue));
            if (sourceWidth <= 0 || sourceHeight <= 0)
                throw new InvalidDataException("Media Foundation returned invalid video dimensions.");

            int outputWidth = Math.Min(ThumbnailWidth, sourceWidth);
            int outputHeight = Math.Max(
                1,
                (int)Math.Round(sourceHeight * (outputWidth / (double)sourceWidth)));

            using IMFMediaType outputType = MediaFactory.MFCreateMediaType();
            outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32).CheckError();
            outputType.Set(
                MediaTypeAttributeKeys.FrameSize,
                ((ulong)(uint)outputWidth << 32) | (uint)outputHeight).CheckError();
            outputType.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1UL << 32) | 1UL).CheckError();
            outputType.Set(MediaTypeAttributeKeys.DefaultStride, checked(outputWidth * 4)).CheckError();
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, outputType);

            List<byte[]> encodedFrames = new(positions.Count);
            for (int index = 0; index < positions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long targetTimestamp = checked(
                    (long)(positions[index].TotalSeconds * MediaFoundationTicksPerSecond));
                reader.SetCurrentPosition(targetTimestamp);

                byte[]? encodedFrame = null;
                while (encodedFrame is null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using IMFSample? sample = reader.ReadSample(
                        SourceReaderIndex.FirstVideoStream,
                        SourceReaderControlFlag.None,
                        out _,
                        out SourceReaderFlag flags,
                        out long timestamp);

                    if (flags.HasFlag(SourceReaderFlag.Error))
                        throw new InvalidDataException("Media Foundation reported a decoding error.");

                    if (flags.HasFlag(SourceReaderFlag.EndOfStream))
                        throw new EndOfStreamException(
                            $"No decoded frame was available at {positions[index]}.");

                    if (sample is not null && timestamp >= targetTimestamp)
                    {
                        using IMFMediaType currentType =
                            reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                        encodedFrame = EncodeSample(sample, currentType);
                    }
                }

                encodedFrames.Add(encodedFrame);
            }

            cancellationToken.ThrowIfCancellationRequested();

            for (int index = 0; index < encodedFrames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fullPath = Path.GetFullPath(outputPaths[index]);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllBytes(fullPath, encodedFrames[index]);
                progress?.Report(new VideoThumbnailResult(index, fullPath));
            }

            Debug.WriteLine(
                $"[Media Foundation] Generated {encodedFrames.Count} thumbnails using the D3D11 device manager.");
            return true;
        }
        finally
        {
            if (mediaFoundationStarted)
                MediaFactory.MFShutdown();

            if (comInitialized)
                CoUninitialize();
        }
    }

    private static byte[] EncodeSample(IMFSample sample, IMFMediaType mediaType)
    {
        ulong packedSize = mediaType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        int width = checked((int)(packedSize >> 32));
        int height = checked((int)(packedSize & uint.MaxValue));
        int stride = unchecked((int)mediaType.GetUInt32(MediaTypeAttributeKeys.DefaultStride));
        if (stride == 0)
            stride = checked(width * 4);

        using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
        buffer.Lock(out IntPtr scanline, out _, out int currentLength);

        try
        {
            int absoluteStride = Math.Abs(stride);
            int requiredLength = checked(absoluteStride * height);
            if (absoluteStride < width * 4 || currentLength < requiredLength)
                throw new InvalidDataException("The decoded RGB frame buffer has an invalid stride or length.");

            using SKBitmap bitmap = new(
                width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);

            byte[] row = new byte[checked(width * 4)];
            IntPtr sourceStart = stride < 0
                ? IntPtr.Add(scanline, checked((height - 1) * absoluteStride))
                : scanline;

            for (int y = 0; y < height; y++)
            {
                IntPtr sourceRow = stride < 0
                    ? IntPtr.Subtract(sourceStart, checked(y * absoluteStride))
                    : IntPtr.Add(sourceStart, checked(y * absoluteStride));
                Marshal.Copy(sourceRow, row, 0, row.Length);
                Marshal.Copy(row, 0, IntPtr.Add(bitmap.GetPixels(), checked(y * (int)bitmap.RowBytes)), row.Length);
            }

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 90)
                ?? throw new InvalidOperationException("SkiaSharp could not encode a Media Foundation frame.");
            return png.ToArray();
        }
        finally
        {
            buffer.Unlock();
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
