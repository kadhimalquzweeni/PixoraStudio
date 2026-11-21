using PixoraStudio.Core.Models;

namespace PixoraStudio.Core.Utilities;

/// <summary>Maps PCI vendor identifiers reported by DXGI to common GPU vendors.</summary>
public static class GraphicsAdapterVendorClassifier
{
    public static GraphicsAdapterVendor Classify(uint vendorId) => vendorId switch
    {
        0x10DE => GraphicsAdapterVendor.Nvidia,
        0x1002 or 0x1022 => GraphicsAdapterVendor.Amd,
        0x8086 => GraphicsAdapterVendor.Intel,
        0x1414 => GraphicsAdapterVendor.Microsoft,
        0x5143 => GraphicsAdapterVendor.Qualcomm,
        0x0000 => GraphicsAdapterVendor.Unknown,
        _ => GraphicsAdapterVendor.Other
    };
}
