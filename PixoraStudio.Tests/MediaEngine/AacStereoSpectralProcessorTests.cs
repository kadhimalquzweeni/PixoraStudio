using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacStereoSpectralProcessorTests
{
    [Fact]
    public void Apply_ShouldReconstructMidSideForSelectedSpectralBands()
    {
        AacIndividualChannelStreamInfo info = LongInfo(maxSfb: 1);
        AacIndividualChannelStreamData left = CreateChannelData(
            new AacScaleFactorData([]));
        AacIndividualChannelStreamData right = CreateChannelData(
            new AacScaleFactorData([]));
        float[] leftSpectrum = new float[1024];
        float[] rightSpectrum = new float[1024];
        leftSpectrum[0] = 3f; leftSpectrum[1] = 4f;
        leftSpectrum[2] = 5f; leftSpectrum[3] = 6f;
        rightSpectrum[0] = 1f; rightSpectrum[1] = 2f;
        rightSpectrum[2] = 3f; rightSpectrum[3] = 4f;
        bool[] mask = new bool[1024];
        mask[0] = true; mask[1] = true;

        new AacStereoSpectralProcessor().Apply(
            leftSpectrum,
            rightSpectrum,
            left,
            left,
            left,
            right,
            AacScaleFactorBandTable.GetLongWindowOffsets(4),
            info,
            mask);

        Assert.Equal(new[] { 4f, 6f, 5f, 6f }, leftSpectrum.Take(4));
        Assert.Equal(new[] { 2f, 2f, 3f, 4f }, rightSpectrum.Take(4));
    }

    [Theory]
    [InlineData(14, -1f)]
    [InlineData(15, 1f)]
    public void Apply_ShouldReconstructIntensityStereo(
        int codebook,
        float expectedSign)
    {
        AacIndividualChannelStreamInfo info = LongInfo(maxSfb: 1);
        AacIndividualChannelStreamData left = CreateChannelData(
            new AacScaleFactorData([]));
        AacIndividualChannelStreamData right = CreateChannelData(
            new AacScaleFactorData([
                new AacScaleFactorBandValue(0, 0, codebook, 4)
            ]));
        float[] leftSpectrum = new float[1024];
        leftSpectrum[0] = 2f; leftSpectrum[1] = 4f;
        leftSpectrum[2] = 6f; leftSpectrum[3] = 8f;
        float[] rightSpectrum = new float[1024];

        new AacStereoSpectralProcessor().Apply(
            leftSpectrum,
            rightSpectrum,
            right,
            AacScaleFactorBandTable.GetLongWindowOffsets(4),
            info,
            new bool[1024]);

        Assert.Equal(expectedSign, (float)Math.Sign(rightSpectrum[0]));
        Assert.Equal(1f, MathF.Abs(rightSpectrum[0]), 5);
        Assert.Equal(2f, MathF.Abs(rightSpectrum[1]), 5);
    }

    [Fact]
    public void Apply_ShouldInvertIntensitySignWhenMsMaskIsSet()
    {
        AacIndividualChannelStreamInfo info = LongInfo(maxSfb: 1);
        AacIndividualChannelStreamData left = CreateChannelData(
            new AacScaleFactorData([]));
        AacIndividualChannelStreamData right = CreateChannelData(
            new AacScaleFactorData([
                new AacScaleFactorBandValue(0, 0, 14, 4)
            ]));
        float[] leftSpectrum = new float[1024];
        leftSpectrum[0] = 2f;
        float[] rightSpectrum = new float[1024];
        bool[] mask = new bool[1024];
        mask[0] = true;

        new AacStereoSpectralProcessor().Apply(
            leftSpectrum,
            rightSpectrum,
            right,
            AacScaleFactorBandTable.GetLongWindowOffsets(4),
            info,
            mask);

        Assert.Equal(-1f, rightSpectrum[0], 5);
    }

    [Fact]
    public void Apply_ShouldCorrelatePnsBandsWhenMsMaskIsSet()
    {
        AacIndividualChannelStreamInfo info = LongInfo(maxSfb: 1);
        AacIndividualChannelStreamData left = CreateChannelData(
            new AacScaleFactorData([
                new AacScaleFactorBandValue(0, 0, 13, 100)
            ]));
        AacIndividualChannelStreamData right = CreateChannelData(
            new AacScaleFactorData([
                new AacScaleFactorBandValue(0, 0, 13, 104)
            ]));
        float[] leftSpectrum = new float[1024];
        float[] rightSpectrum = new float[1024];
        leftSpectrum[0] = 1f;
        leftSpectrum[1] = -0.5f;
        rightSpectrum[0] = 0.1f;
        rightSpectrum[1] = 0.2f;
        bool[] mask = new bool[1024];
        Array.Fill(mask, true, 0, 4);

        new AacStereoSpectralProcessor().Apply(
            leftSpectrum,
            rightSpectrum,
            left,
            right,
            AacScaleFactorBandTable.GetLongWindowOffsets(4),
            info,
            mask);

        Assert.Equal(1f, leftSpectrum[0]);
        Assert.Equal(-0.5f, leftSpectrum[1]);
        Assert.Equal(2f, rightSpectrum[0]);
        Assert.Equal(-1f, rightSpectrum[1]);
    }

    private static AacIndividualChannelStreamInfo LongInfo(byte maxSfb) =>
        new(AacWindowSequence.OnlyLongSequence, AacWindowShape.Sine, maxSfb, 0, false);

    private static AacIndividualChannelStreamData CreateChannelData(
        AacScaleFactorData scaleFactors) =>
        new(
            100,
            LongInfo(1),
            Array.Empty<AacSectionInfo>(),
            scaleFactors,
            null,
            new AacTnsData([]),
            Array.Empty<AacSpectralBandData>());
}
