using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Extracts video frames through Media Foundation's Source Reader with a D3D11
/// device manager attached. This enables DXVA-capable H.264 decoder transforms.
/// GPU-backed samples are copied to a staging texture only for CPU PNG encoding.
/// </summary>
public sealed class MediaFoundationVideoThumbnailDecoder : IHardwareVideoThumbnailDecoder
{
    private const int ThumbnailWidth = 240;
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

        if (positions.Count != outputPaths.Count)
            throw new ArgumentException("Positions and output paths must have matching counts.");
        if (!OperatingSystem.IsWindows())
            return Task.FromResult(false);
        if (positions.Count == 0)
            return Task.FromResult(true);

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

            using D3D11DeviceBundle deviceBundle =
                D3D11AdapterDiscovery.CreateBestDevice();

            ID3D11Device d3dDevice = deviceBundle.Device;
            ID3D11DeviceContext d3dContext = deviceBundle.Context;

            using ID3D11Multithread multithread =
                d3dContext.QueryInterface<ID3D11Multithread>();
            multithread.SetMultithreadProtected(true);

            Debug.WriteLine(
                $"[Media Foundation] Selected GPU: {deviceBundle.Adapter.Name} " +
                $"({deviceBundle.Adapter.Vendor}, vendorId=0x{deviceBundle.Adapter.VendorId:X4}, " +
                $"deviceId=0x{deviceBundle.Adapter.DeviceId:X4}, " +
                $"dedicatedMemory={deviceBundle.Adapter.DedicatedVideoMemoryBytes / (1024 * 1024)} MB).");

            return GenerateWithDevice(
                d3dDevice, d3dContext, filePath, positions, outputPaths,
                progress, cancellationToken);
        }
        finally
        {
            if (mediaFoundationStarted)
                MediaFactory.MFShutdown();

            if (comInitialized)
                CoUninitialize();
        }
    }

    private static bool GenerateWithDevice(
        ID3D11Device d3dDevice,
        ID3D11DeviceContext d3dContext,
        string filePath,
        IReadOnlyList<TimeSpan> positions,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress,
        CancellationToken cancellationToken)
    {
        using IMFDXGIDeviceManager deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
        deviceManager.ResetDevice(d3dDevice).CheckError();

        using IMFAttributes attributes = MediaFactory.MFCreateAttributes(4);
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
                positions[index].Ticks);
            reader.SetCurrentPosition(targetTimestamp);

            byte[]? encodedFrame = null;
            IMFSample? previousSample = null;
            try
            {
                while (encodedFrame is null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using IMFSample? sample = reader.ReadSample(
                        SourceReaderIndex.FirstVideoStream,
                        SourceReaderControlFlag.None,
                        out _,
                        out SourceReaderFlag flags,
                        out long timestamp);

                    SampleReadDisposition disposition = GetReadDisposition(
                        sample is not null,
                        previousSample is not null,
                        flags.HasFlag(SourceReaderFlag.Error),
                        flags.HasFlag(SourceReaderFlag.EndOfStream),
                        timestamp,
                        targetTimestamp);

                    switch (disposition)
                    {
                        case SampleReadDisposition.Error:
                            throw new InvalidDataException(
                                "Media Foundation reported a decoding error.");

                        case SampleReadDisposition.EndOfStream:
                            throw new EndOfStreamException(
                                $"No decoded frame was available at {positions[index]}.");

                        case SampleReadDisposition.Accept:
                            encodedFrame = EncodeReaderSample(
                                sample!, d3dDevice, d3dContext, reader);
                            break;

                        case SampleReadDisposition.AcceptLastAvailable:
                            // The final displayed frame usually starts before the
                            // media duration. Use it when the target is the final
                            // filmstrip position and no later frame exists.
                            encodedFrame = EncodeReaderSample(
                                sample ?? previousSample!, d3dDevice, d3dContext, reader);
                            break;

                        case SampleReadDisposition.Skip:
                            // Keep only the latest preroll frame. If the requested
                            // position falls after the final frame timestamp, it
                            // becomes the last-frame fallback.
                            if (sample is not null)
                            {
                                previousSample?.Dispose();
                                previousSample = sample.QueryInterface<IMFSample>();
                            }
                            break;
                    }
                }
            }
            finally
            {
                previousSample?.Dispose();
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
            $"[Media Foundation] Generated {encodedFrames.Count} thumbnails with the D3D11 device manager and DXVA enabled.");
        return true;
    }


    internal enum SampleReadDisposition
    {
        Skip,
        Accept,
        AcceptLastAvailable,
        EndOfStream,
        Error
    }

    /// <summary>
    /// Determines how a Source Reader result should be handled. The final
    /// displayed frame can have a timestamp earlier than the media duration,
    /// so the latest decoded frame is retained as a fallback for end targets.
    /// </summary>
    internal static SampleReadDisposition GetReadDisposition(
        bool hasSample,
        bool hasPreviousSample,
        bool hasError,
        bool endOfStream,
        long sampleTimestamp,
        long targetTimestamp)
    {
        if (hasError)
            return SampleReadDisposition.Error;

        if (hasSample && sampleTimestamp >= targetTimestamp)
            return SampleReadDisposition.Accept;

        if (endOfStream && (hasSample || hasPreviousSample))
            return SampleReadDisposition.AcceptLastAvailable;

        return endOfStream
            ? SampleReadDisposition.EndOfStream
            : SampleReadDisposition.Skip;
    }

    private static byte[] EncodeReaderSample(
        IMFSample sample,
        ID3D11Device d3dDevice,
        ID3D11DeviceContext d3dContext,
        IMFSourceReader reader)
    {
        using IMFMediaType currentType =
            reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        return EncodeSample(sample, currentType, d3dDevice, d3dContext);
    }

    private static byte[] EncodeSample(
        IMFSample sample,
        IMFMediaType mediaType,
        ID3D11Device d3dDevice,
        ID3D11DeviceContext d3dContext)
    {
        using SKBitmap bitmap = CopySampleToBitmap(sample, mediaType, d3dDevice, d3dContext);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 90)
            ?? throw new InvalidOperationException("SkiaSharp could not encode a Media Foundation frame.");
        return png.ToArray();
    }

    /// <summary>
    /// Copies a decoded Source Reader sample (GPU texture or system memory)
    /// into a BGRA bitmap the caller owns.
    /// </summary>
    internal static SKBitmap CopySampleToBitmap(
        IMFSample sample,
        IMFMediaType mediaType,
        ID3D11Device d3dDevice,
        ID3D11DeviceContext d3dContext)
    {
        ulong packedSize = mediaType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        int width = checked((int)(packedSize >> 32));
        int height = checked((int)(packedSize & uint.MaxValue));

        using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
        using IMFDXGIBuffer? dxgiBuffer = buffer.QueryInterfaceOrNull<IMFDXGIBuffer>();
        SKBitmap bitmap = new(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        try
        {
            if (dxgiBuffer is not null)
                CopyDxgiSurfaceToBitmap(dxgiBuffer, d3dDevice, d3dContext, bitmap, width, height);
            else
                CopySystemMemoryBufferToBitmap(buffer, mediaType, bitmap, width, height);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void CopyDxgiSurfaceToBitmap(
        IMFDXGIBuffer dxgiBuffer,
        ID3D11Device d3dDevice,
        ID3D11DeviceContext d3dContext,
        SKBitmap bitmap,
        int width,
        int height)
    {
        (IntPtr texturePointer, uint subresource) = GetDxgiTexture(dxgiBuffer);
        using ID3D11Texture2D sourceTexture = new(texturePointer);

        Texture2DDescription description = sourceTexture.Description;
        using ID3D11Texture2D stagingTexture = d3dDevice.CreateTexture2D(
            description.Format,
            description.Width,
            description.Height,
            description.ArraySize,
            description.MipLevels,
            bindFlags: BindFlags.None,
            miscFlags: ResourceOptionFlags.None,
            usage: ResourceUsage.Staging,
            cpuAccessFlags: CpuAccessFlags.Read);

        d3dContext.CopyResource(stagingTexture, sourceTexture);
        MappedSubresource mapped = d3dContext.Map(stagingTexture, subresource, MapMode.Read);
        try
        {
            CopyRowsToBitmap(
                mapped.DataPointer,
                checked((int)mapped.RowPitch),
                bitmap,
                width,
                height);
        }
        finally
        {
            d3dContext.Unmap(stagingTexture, subresource);
        }
    }


    private static (IntPtr TexturePointer, uint Subresource) GetDxgiTexture(
        IMFDXGIBuffer dxgiBuffer)
    {
        IntPtr nativePointer = dxgiBuffer.NativePointer;
        IntPtr vtable = Marshal.ReadIntPtr(nativePointer);

        IntPtr getResourceAddress = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        var getResource = Marshal.GetDelegateForFunctionPointer<GetDxgiResourceDelegate>(
            getResourceAddress);

        Guid textureInterfaceId = typeof(ID3D11Texture2D).GUID;
        int resourceResult = getResource(
            nativePointer, ref textureInterfaceId, out IntPtr texturePointer);
        Marshal.ThrowExceptionForHR(resourceResult);

        IntPtr getSubresourceAddress = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
        var getSubresource = Marshal.GetDelegateForFunctionPointer<GetDxgiSubresourceDelegate>(
            getSubresourceAddress);

        int subresourceResult = getSubresource(nativePointer, out uint subresource);
        if (subresourceResult < 0)
        {
            Marshal.Release(texturePointer);
            Marshal.ThrowExceptionForHR(subresourceResult);
        }

        return (texturePointer, subresource);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDxgiResourceDelegate(
        IntPtr self,
        ref Guid interfaceId,
        out IntPtr resource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDxgiSubresourceDelegate(
        IntPtr self,
        out uint subresource);

    private static void CopySystemMemoryBufferToBitmap(
        IMFMediaBuffer buffer,
        IMFMediaType mediaType,
        SKBitmap bitmap,
        int width,
        int height)
    {
        int stride = unchecked((int)mediaType.GetUInt32(MediaTypeAttributeKeys.DefaultStride));
        if (stride == 0)
            stride = checked(width * 4);

        buffer.Lock(out IntPtr scanline, out _, out int currentLength);
        try
        {
            int absoluteStride = Math.Abs(stride);
            int requiredLength = checked(absoluteStride * height);
            if (absoluteStride < width * 4 || currentLength < requiredLength)
                throw new InvalidDataException("The decoded RGB frame buffer has an invalid stride or length.");

            IntPtr firstRow = stride < 0
                ? IntPtr.Add(scanline, checked((height - 1) * absoluteStride))
                : scanline;

            for (int y = 0; y < height; y++)
            {
                IntPtr row = stride < 0
                    ? IntPtr.Subtract(firstRow, checked(y * absoluteStride))
                    : IntPtr.Add(firstRow, checked(y * absoluteStride));
                CopyRow(row, bitmap, y, width);
            }
        }
        finally
        {
            buffer.Unlock();
        }
    }

    private static void CopyRowsToBitmap(
        IntPtr source,
        int sourceStride,
        SKBitmap bitmap,
        int width,
        int height)
    {
        if (sourceStride < width * 4)
            throw new InvalidDataException("The DXGI staging texture has an invalid row pitch.");

        for (int y = 0; y < height; y++)
            CopyRow(IntPtr.Add(source, checked(y * sourceStride)), bitmap, y, width);
    }

    private static void CopyRow(IntPtr source, SKBitmap bitmap, int y, int width)
    {
        byte[] row = new byte[checked(width * 4)];
        Marshal.Copy(source, row, 0, row.Length);
        Marshal.Copy(
            row,
            0,
            IntPtr.Add(bitmap.GetPixels(), checked(y * (int)bitmap.RowBytes)),
            row.Length);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
