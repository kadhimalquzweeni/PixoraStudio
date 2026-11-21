using PixoraStudio.Core.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Enumerates DXGI adapters and creates D3D11 devices on candidate hardware.
/// Software adapters are never selected for the hardware thumbnail pipeline.
/// </summary>
internal static class D3D11AdapterDiscovery
{
    private static readonly FeatureLevel[] SupportedFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    ];

    public static IReadOnlyList<GraphicsAdapterCapabilities> ProbeAdapters()
    {
        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        List<GraphicsAdapterCapabilities> adapters = [];

        for (uint index = 0;
             factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Success;
             index++)
        {
            if (adapter is null)
                continue;

            using (adapter)
            {
                AdapterDescription1 description = adapter.Description1;
                bool isSoftware = (description.Flags & AdapterFlags.Software) != 0;
                bool canCreateDevice = !isSoftware &&
                    TryCreateDevice(adapter, out _, out _);

                adapters.Add(CreateCapabilities(
                    checked((int)index),
                    description,
                    isSoftware,
                    canCreateDevice));
            }
        }

        return adapters;
    }

    public static D3D11DeviceBundle CreateBestDevice()
    {
        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        List<(int Index, AdapterDescription1 Description)> candidates = [];

        for (uint index = 0;
             factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Success;
             index++)
        {
            if (adapter is null)
                continue;

            using (adapter)
            {
                AdapterDescription1 description = adapter.Description1;
                if ((description.Flags & AdapterFlags.Software) == 0)
                    candidates.Add((checked((int)index), description));
            }
        }

        foreach ((int index, AdapterDescription1 description) in candidates
                     .OrderByDescending(item => Convert.ToUInt64(item.Description.DedicatedVideoMemory))
                     .ThenBy(item => item.Index))
        {
            using IDXGIAdapter1 adapter = GetAdapter(factory, checked((uint)index));

            Result result = D3D11.D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                SupportedFeatureLevels,
                out ID3D11Device device,
                out _,
                out ID3D11DeviceContext context);

            if (result.Success)
            {
                GraphicsAdapterCapabilities capabilities = CreateCapabilities(
                    index, description, false, true);
                return new D3D11DeviceBundle(device, context, capabilities);
            }

            device?.Dispose();
            context?.Dispose();
        }

        throw new InvalidOperationException(
            "No hardware graphics adapter could create a Direct3D 11 video device.");
    }

    private static IDXGIAdapter1 GetAdapter(IDXGIFactory1 factory, uint index)
    {
        factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).CheckError();
        return adapter ?? throw new InvalidOperationException(
            $"DXGI did not return graphics adapter {index}.");
    }

    private static bool TryCreateDevice(
        IDXGIAdapter1 adapter,
        out ID3D11Device? device,
        out ID3D11DeviceContext? context)
    {
        Result result = D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            SupportedFeatureLevels,
            out ID3D11Device createdDevice,
            out _,
            out ID3D11DeviceContext createdContext);

        if (result.Success)
        {
            device = createdDevice;
            context = createdContext;
            device.Dispose();
            context.Dispose();
            return true;
        }

        createdDevice?.Dispose();
        createdContext?.Dispose();
        device = null;
        context = null;
        return false;
    }

    private static GraphicsAdapterCapabilities CreateCapabilities(
        int index,
        AdapterDescription1 description,
        bool isSoftware,
        bool canCreateDevice) =>
        new(
            index,
            description.Description.TrimEnd('\0'),
            description.VendorId,
            description.DeviceId,
            Convert.ToUInt64(description.DedicatedVideoMemory),
            Convert.ToUInt64(description.SharedSystemMemory),
            isSoftware,
            canCreateDevice);
}

internal sealed class D3D11DeviceBundle(
    ID3D11Device device,
    ID3D11DeviceContext context,
    GraphicsAdapterCapabilities adapter) : IDisposable
{
    public ID3D11Device Device { get; } = device;
    public ID3D11DeviceContext Context { get; } = context;
    public GraphicsAdapterCapabilities Adapter { get; } = adapter;

    public void Dispose()
    {
        Context.Dispose();
        Device.Dispose();
    }
}
