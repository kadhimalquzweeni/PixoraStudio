using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using Vortice.MediaFoundation;
using System.Runtime.InteropServices;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Detects the available DXGI adapters, probes D3D11 video-device creation,
/// and checks whether Media Foundation can be started on this Windows system.
/// </summary>
public sealed class MediaFoundationHardwareVideoCapabilityService
    : IHardwareVideoCapabilityService
{
    private const uint CoInitializeMultithreaded = 0;
    private const int RpcEChangedMode = unchecked((int)0x80010106);

    public Task<HardwareVideoCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new HardwareVideoCapabilities(
                false,
                false,
                [],
                null,
                "Hardware video decoding is available only on Windows."));
        }

        return Task.Run(
            () => DetectCapabilities(cancellationToken),
            cancellationToken);
    }

    private static HardwareVideoCapabilities DetectCapabilities(
        CancellationToken cancellationToken)
    {
        int comResult = CoInitializeEx(IntPtr.Zero, CoInitializeMultithreaded);
        bool shouldUninitializeCom = comResult >= 0;
        bool mediaFoundationStarted = false;

        try
        {
            if (comResult < 0 && comResult != RpcEChangedMode)
                Marshal.ThrowExceptionForHR(comResult);

            cancellationToken.ThrowIfCancellationRequested();

            bool mediaFoundationAvailable;
            try
            {
                MediaFactory.MFStartup().CheckError();
                mediaFoundationStarted = true;
                mediaFoundationAvailable = true;
            }
            catch
            {
                mediaFoundationAvailable = false;
            }

            IReadOnlyList<GraphicsAdapterCapabilities> adapters =
                D3D11AdapterDiscovery.ProbeAdapters();
            cancellationToken.ThrowIfCancellationRequested();

            GraphicsAdapterCapabilities? selected =
                GraphicsAdapterSelector.SelectPreferred(adapters);

            string status = !mediaFoundationAvailable
                ? "Media Foundation could not be initialised."
                : selected is null
                    ? "Media Foundation is available, but no hardware D3D11 video adapter could be created."
                    : $"Hardware decoding can be attempted using {selected.Name} ({selected.Vendor}). " +
                      "Codec and profile support is confirmed when a video is opened.";

            return new HardwareVideoCapabilities(
                true,
                mediaFoundationAvailable,
                adapters,
                selected,
                status);
        }
        finally
        {
            if (mediaFoundationStarted)
                MediaFactory.MFShutdown();

            if (shouldUninitializeCom)
                CoUninitialize();
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
