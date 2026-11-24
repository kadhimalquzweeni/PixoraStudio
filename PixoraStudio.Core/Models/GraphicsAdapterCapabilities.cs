using PixoraStudio.Core.Utilities;

namespace PixoraStudio.Core.Models;

/// <summary>Hardware and driver information reported for a DXGI graphics adapter.</summary>
public sealed record GraphicsAdapterCapabilities(
    int AdapterIndex,
    string Name,
    uint VendorId,
    uint DeviceId,
    ulong DedicatedVideoMemoryBytes,
    ulong SharedSystemMemoryBytes,
    bool IsSoftwareAdapter,
    bool CanCreateD3D11Device)
{
    public GraphicsAdapterVendor Vendor =>
        GraphicsAdapterVendorClassifier.Classify(VendorId);
}
