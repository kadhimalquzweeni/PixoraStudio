using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DecodedPictureBufferLimitsTests
{
    [Theory]
    [InlineData(30, 1, 1, 16)]
    [InlineData(30, 20, 15, 16)]
    [InlineData(30, 40, 30, 6)]
    [InlineData(31, 80, 45, 5)]
    [InlineData(40, 120, 68, 4)]
    [InlineData(51, 240, 135, 5)]
    public void ResolveMaxDpbFrames_ShouldUseLevelAndPictureMacroblockDimensions(
        byte levelIdc,
        uint widthInMacroblocks,
        uint heightInMacroblocks,
        int expected)
    {
        H264SequenceParameterSet sps = CreateSps(
            levelIdc,
            widthInMacroblocks,
            heightInMacroblocks);

        Assert.Equal(expected, H264DecodedPictureBufferLimits.ResolveMaxDpbFrames(sps));
    }

    [Fact]
    public void ResolveMaxDpbFrames_ShouldCapAtSixteenFrames()
    {
        H264SequenceParameterSet sps = CreateSps(30, 1, 1);

        Assert.Equal(16, H264DecodedPictureBufferLimits.ResolveMaxDpbFrames(sps));
    }

    [Fact]
    public void ResolveMaxDpbFrames_ShouldUseLevelOneBForConstraintSetThree()
    {
        H264SequenceParameterSet sps = CreateSps(
            11, 1, 1, profileIdc: 66, profileCompatibility: 0x10);

        Assert.Equal(16, H264DecodedPictureBufferLimits.ResolveMaxDpbFrames(sps));
    }

    [Fact]
    public void ResolveMaxDpbFrames_ShouldUseLevelOneBLevelIdcNine()
    {
        H264SequenceParameterSet sps = CreateSps(9, 1, 1, profileIdc: 100);

        Assert.Equal(16, H264DecodedPictureBufferLimits.ResolveMaxDpbFrames(sps));
    }

    [Fact]
    public void ResolveMaxDpbFrames_ShouldRejectPictureLargerThanLevelAllows()
    {
        H264SequenceParameterSet sps = CreateSps(10, 10, 10);

        Assert.Throws<FormatException>(() =>
            H264DecodedPictureBufferLimits.ResolveMaxDpbFrames(sps));
    }

    [Fact]
    public void ResolveMaxDpbFrames_ShouldRejectUnknownLevel()
    {
        H264SequenceParameterSet sps = CreateSps(99, 1, 1);

        Assert.Throws<NotSupportedException>(() =>
            H264DecodedPictureBufferLimits.ResolveMaxDpbFrames(sps));
    }

    [Fact]
    public void ResolveMaxDpbFrames_ShouldRejectNullSps()
    {
        Assert.Throws<ArgumentNullException>(() =>
            H264DecodedPictureBufferLimits.ResolveMaxDpbFrames(null!));
    }

    private static H264SequenceParameterSet CreateSps(
        byte levelIdc,
        uint widthInMacroblocks,
        uint heightInMacroblocks,
        byte profileIdc = 66,
        byte profileCompatibility = 0)
    {
        return new H264SequenceParameterSet(
            0,
            profileIdc,
            levelIdc,
            1,
            false,
            0,
            0,
            0,
            0,
            0,
            1,
            widthInMacroblocks - 1,
            heightInMacroblocks - 1,
            true,
            true,
            checked(widthInMacroblocks * 16),
            checked(heightInMacroblocks * 16),
            false)
        {
            ProfileCompatibility = profileCompatibility
        };
    }
}
