using PixoraStudio.Core.Models;
using PixoraStudio.Core.Utilities;
using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class HardwareVideoCapabilityTests
{
    [Theory]
    [InlineData(0x10DE, GraphicsAdapterVendor.Nvidia)]
    [InlineData(0x1002, GraphicsAdapterVendor.Amd)]
    [InlineData(0x1022, GraphicsAdapterVendor.Amd)]
    [InlineData(0x8086, GraphicsAdapterVendor.Intel)]
    [InlineData(0x1414, GraphicsAdapterVendor.Microsoft)]
    [InlineData(0x5143, GraphicsAdapterVendor.Qualcomm)]
    [InlineData(0x0000, GraphicsAdapterVendor.Unknown)]
    [InlineData(0x1234, GraphicsAdapterVendor.Other)]
    public void Classify_ShouldMapKnownVendorIdentifiers(
        uint vendorId,
        GraphicsAdapterVendor expected)
    {
        Assert.Equal(expected, GraphicsAdapterVendorClassifier.Classify(vendorId));
    }

    [Fact]
    public void SelectPreferred_ShouldChooseUsableDiscreteAdapterOverIntegratedAdapter()
    {
        GraphicsAdapterCapabilities integrated = new(
            0, "Integrated", 0x8086, 1, 0, 8UL * 1024 * 1024 * 1024,
            false, true);
        GraphicsAdapterCapabilities discrete = new(
            1, "Discrete", 0x10DE, 2, 8UL * 1024 * 1024 * 1024, 0,
            false, true);

        GraphicsAdapterCapabilities? selected =
            GraphicsAdapterSelector.SelectPreferred([integrated, discrete]);

        Assert.Same(discrete, selected);
    }

    [Fact]
    public void SelectPreferred_ShouldIgnoreSoftwareAdapters()
    {
        GraphicsAdapterCapabilities software = new(
            0, "Microsoft Basic Render Driver", 0x1414, 0x008C, 0, 0,
            true, true);

        Assert.Null(GraphicsAdapterSelector.SelectPreferred([software]));
    }

    [Fact]
    public void SelectPreferred_ShouldIgnoreAdaptersThatCannotCreateD3D11Device()
    {
        GraphicsAdapterCapabilities unavailable = new(
            0, "Unavailable", 0x10DE, 1, 12UL * 1024 * 1024 * 1024, 0,
            false, false);
        GraphicsAdapterCapabilities available = new(
            1, "Available", 0x8086, 2, 0, 8UL * 1024 * 1024 * 1024,
            false, true);

        Assert.Same(
            available,
            GraphicsAdapterSelector.SelectPreferred([unavailable, available]));
    }

    [Fact]
    public void HardwareVideoCapabilities_ShouldRequireWindowsMediaFoundationAndUsableAdapter()
    {
        GraphicsAdapterCapabilities adapter = new(
            0, "Intel GPU", 0x8086, 1, 0, 8UL * 1024 * 1024 * 1024,
            false, true);

        HardwareVideoCapabilities ready = new(
            true, true, [adapter], adapter, "Ready");
        HardwareVideoCapabilities noMediaFoundation = new(
            true, false, [adapter], adapter, "No MF");
        HardwareVideoCapabilities noAdapter = new(
            true, true, [], null, "No adapter");
        HardwareVideoCapabilities nonWindows = new(
            false, false, [], null, "Not Windows");

        Assert.True(ready.CanAttemptHardwareDecoding);
        Assert.False(noMediaFoundation.CanAttemptHardwareDecoding);
        Assert.False(noAdapter.CanAttemptHardwareDecoding);
        Assert.False(nonWindows.CanAttemptHardwareDecoding);
    }
}
