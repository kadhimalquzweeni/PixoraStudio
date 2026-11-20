using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoExportEligibilityTests
{
    [Fact]
    public void CanExport_ShouldAllowMp4WithValidTrimEvenWhileThumbnailsAreStillGenerating()
    {
        // Thumbnail generation is intentionally not an input to export eligibility.
        bool canExport = VideoExportEligibility.CanExport(
            @"C:\\Videos\\source.mp4",
            isExporting: false,
            trimStartMilliseconds: 1_000,
            trimEndMilliseconds: 5_000);

        Assert.True(canExport);
    }

    [Fact]
    public void CanExport_ShouldRejectWhenAnExportIsAlreadyRunning()
    {
        bool canExport = VideoExportEligibility.CanExport(
            @"C:\\Videos\\source.mp4",
            isExporting: true,
            trimStartMilliseconds: 1_000,
            trimEndMilliseconds: 5_000);

        Assert.False(canExport);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\\Videos\\source.mov")]
    [InlineData(@"C:\\Videos\\source.mkv")]
    public void CanExport_ShouldRejectMissingOrUnsupportedSource(string? videoPath)
    {
        bool canExport = VideoExportEligibility.CanExport(
            videoPath,
            isExporting: false,
            trimStartMilliseconds: 0,
            trimEndMilliseconds: 5_000);

        Assert.False(canExport);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5_000, 1_000)]
    [InlineData(-1, 1_000)]
    [InlineData(0, double.NaN)]
    [InlineData(double.NaN, 1_000)]
    [InlineData(0, double.PositiveInfinity)]
    public void CanExport_ShouldRejectInvalidTrimRange(
        double trimStartMilliseconds,
        double trimEndMilliseconds)
    {
        bool canExport = VideoExportEligibility.CanExport(
            @"C:\\Videos\\source.mp4",
            isExporting: false,
            trimStartMilliseconds,
            trimEndMilliseconds);

        Assert.False(canExport);
    }

    [Fact]
    public void CanExport_ShouldAcceptMp4ExtensionCaseInsensitively()
    {
        bool canExport = VideoExportEligibility.CanExport(
            @"C:\\Videos\\source.MP4",
            isExporting: false,
            trimStartMilliseconds: 0,
            trimEndMilliseconds: 1);

        Assert.True(canExport);
    }
}
