using SkiaSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>A decoded frame as top-down BGRA pixels.</summary>
public sealed record VideoFramePixels(int Width, int Height, int Stride, byte[] Bgra);

/// <summary>
/// Keeps one Media Foundation Source Reader (D3D11 device manager, DXVA
/// enabled) open for a video so exact frames can be fetched quickly while
/// scrubbing. Opening the file is the expensive part; after that each request
/// is a seek plus GPU decode from the preceding keyframe. All Media Foundation
/// work runs on one dedicated thread, in request order.
/// </summary>
public sealed class MediaFoundationFrameReader : IDisposable
{
    private const uint CoInitializeMultithreaded = 0;

    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private int _disposed;

    // Worker-thread state.
    private IMFSourceReader? _reader;
    private D3D11DeviceBundle? _deviceBundle;
    private bool _mediaFoundationStarted;

    private MediaFoundationFrameReader()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Media Foundation frame reader"
        };
        _thread.Start();
    }

    /// <summary>
    /// Opens <paramref name="filePath"/> for random frame access, scaled so the
    /// output is at most <paramref name="maxWidth"/> pixels wide. Returns null
    /// when Media Foundation or a hardware device is unavailable.
    /// </summary>
    public static async Task<MediaFoundationFrameReader?> TryOpenAsync(
        string filePath,
        int maxWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);

        if (!OperatingSystem.IsWindows())
            return null;

        MediaFoundationFrameReader instance = new();
        try
        {
            TaskCompletionSource<bool> opened = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            instance._work.Add(() =>
            {
                try
                {
                    instance.Open(Path.GetFullPath(filePath), maxWidth);
                    opened.SetResult(true);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(
                        $"[Media Foundation] Frame reader could not open the video: {exception.Message}");
                    opened.SetResult(false);
                }
            });

            using (cancellationToken.Register(() => opened.TrySetCanceled(cancellationToken)))
            {
                if (await opened.Task.ConfigureAwait(false))
                    return instance;
            }
        }
        catch
        {
            instance.Dispose();
            throw;
        }

        instance.Dispose();
        return null;
    }

    /// <summary>
    /// Returns the frame displayed at <paramref name="position"/>. Requests run
    /// in order; a request whose token is already cancelled is skipped.
    /// </summary>
    public Task<VideoFramePixels> ReadFrameAsync(
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, TimeSpan.Zero);

        TaskCompletionSource<VideoFramePixels> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _work.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    completion.TrySetResult(ReadFrame(position, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
        }
        catch (InvalidOperationException)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(MediaFoundationFrameReader)));
        }

        return completion.Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // The worker drains queued requests, then releases its COM objects.
        _work.CompleteAdding();
    }

    private void Run()
    {
        int comResult = CoInitializeEx(IntPtr.Zero, CoInitializeMultithreaded);
        bool comInitialized = comResult >= 0;

        try
        {
            foreach (Action item in _work.GetConsumingEnumerable())
                item();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Media Foundation] Frame reader stopped: {exception.Message}");
        }
        finally
        {
            _reader?.Dispose();
            _reader = null;
            _deviceBundle?.Dispose();
            _deviceBundle = null;

            if (_mediaFoundationStarted)
                MediaFactory.MFShutdown();
            if (comInitialized)
                CoUninitialize();
        }
    }

    private void Open(string filePath, int maxWidth)
    {
        MediaFactory.MFStartup().CheckError();
        _mediaFoundationStarted = true;

        _deviceBundle = D3D11AdapterDiscovery.CreateBestDevice();
        using ID3D11Multithread multithread =
            _deviceBundle.Context.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);

        using IMFDXGIDeviceManager deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
        deviceManager.ResetDevice(_deviceBundle.Device).CheckError();

        using IMFAttributes attributes = MediaFactory.MFCreateAttributes(4);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1).CheckError();
        attributes.Set(SourceReaderAttributeKeys.D3DManager, deviceManager).CheckError();
        attributes.Set(SourceReaderAttributeKeys.DisableDxva, 0).CheckError();
        attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, true).CheckError();

        IMFSourceReader reader = MediaFactory.MFCreateSourceReaderFromURL(filePath, attributes);
        try
        {
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

            using IMFMediaType nativeType =
                reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
            ulong nativeSize = nativeType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            int sourceWidth = checked((int)(nativeSize >> 32));
            int sourceHeight = checked((int)(nativeSize & uint.MaxValue));
            if (sourceWidth <= 0 || sourceHeight <= 0)
                throw new InvalidDataException("Media Foundation returned invalid video dimensions.");

            int outputWidth = Math.Min(maxWidth, sourceWidth);
            int outputHeight = Math.Max(
                1, (int)Math.Round(sourceHeight * (outputWidth / (double)sourceWidth)));

            using IMFMediaType outputType = MediaFactory.MFCreateMediaType();
            outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32).CheckError();
            outputType.Set(
                MediaTypeAttributeKeys.FrameSize,
                ((ulong)(uint)outputWidth << 32) | (uint)outputHeight).CheckError();
            outputType.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1UL << 32) | 1UL).CheckError();
            outputType.Set(MediaTypeAttributeKeys.DefaultStride, checked(outputWidth * 4)).CheckError();
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, outputType);
        }
        catch
        {
            reader.Dispose();
            throw;
        }

        _reader = reader;
        Debug.WriteLine(
            $"[Media Foundation] Frame reader open on {_deviceBundle.Adapter.Name} (DXVA enabled).");
    }

    private VideoFramePixels ReadFrame(TimeSpan position, CancellationToken cancellationToken)
    {
        IMFSourceReader reader = _reader ??
            throw new InvalidOperationException("The frame reader is not open.");
        D3D11DeviceBundle devices = _deviceBundle!;

        long targetTimestamp = position.Ticks;
        reader.SetCurrentPosition(targetTimestamp);

        IMFSample? previousSample = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using IMFSample? sample = reader.ReadSample(
                    SourceReaderIndex.FirstVideoStream,
                    SourceReaderControlFlag.None,
                    out _,
                    out SourceReaderFlag flags,
                    out long timestamp);

                MediaFoundationVideoThumbnailDecoder.SampleReadDisposition disposition =
                    MediaFoundationVideoThumbnailDecoder.GetReadDisposition(
                        sample is not null,
                        previousSample is not null,
                        flags.HasFlag(SourceReaderFlag.Error),
                        flags.HasFlag(SourceReaderFlag.EndOfStream),
                        timestamp,
                        targetTimestamp);

                switch (disposition)
                {
                    case MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Error:
                        throw new InvalidDataException("Media Foundation reported a decoding error.");

                    case MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.EndOfStream:
                        throw new EndOfStreamException($"No decoded frame was available at {position}.");

                    case MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.Accept:
                        return ToPixels(sample!, reader, devices);

                    case MediaFoundationVideoThumbnailDecoder.SampleReadDisposition.AcceptLastAvailable:
                        return ToPixels(sample ?? previousSample!, reader, devices);

                    default:
                        // Preroll between the keyframe and the target: keep only
                        // the latest picture as the end-of-stream fallback.
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
    }

    private static VideoFramePixels ToPixels(
        IMFSample sample,
        IMFSourceReader reader,
        D3D11DeviceBundle devices)
    {
        using IMFMediaType currentType =
            reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        using SKBitmap bitmap = MediaFoundationVideoThumbnailDecoder.CopySampleToBitmap(
            sample, currentType, devices.Device, devices.Context);
        return new VideoFramePixels(
            bitmap.Width, bitmap.Height, bitmap.RowBytes, bitmap.Bytes);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
