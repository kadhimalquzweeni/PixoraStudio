using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraPredictionModeReaderTests
{
    [Fact]
    public void Read_ShouldReadSixteen4x4ModeEntries()
    {
        string bits = "1" + "0" + "101" + string.Concat(Enumerable.Repeat("1", 14));
        H264BitReader reader = CreateReader(bits);

        var result = new H264IntraPredictionModeReader().Read(reader, false);

        Assert.False(result.Uses8X8Transform);
        Assert.Equal(16, result.Modes.Count);
        Assert.True(result.Modes[0].PreviousModeFlag);
        Assert.Null(result.Modes[0].RemainderMode);
        Assert.False(result.Modes[1].PreviousModeFlag);
        Assert.Equal(5u, result.Modes[1].RemainderMode);
        Assert.All(result.Modes.Skip(2), mode =>
        {
            Assert.True(mode.PreviousModeFlag);
            Assert.Null(mode.RemainderMode);
        });
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void Read_ShouldReadFour8x8ModeEntries()
    {
        H264BitReader reader = CreateReader("0" + "011" + "1" + "0" + "000" + "1");

        var result = new H264IntraPredictionModeReader().Read(reader, true);

        Assert.True(result.Uses8X8Transform);
        Assert.Equal(4, result.Modes.Count);
        Assert.Equal(3u, result.Modes[0].RemainderMode);
        Assert.True(result.Modes[1].PreviousModeFlag);
        Assert.Equal(0u, result.Modes[2].RemainderMode);
        Assert.True(result.Modes[3].PreviousModeFlag);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Theory]
    [InlineData(true, null, 4, 4)]
    [InlineData(false, 2, 4, 3)]
    [InlineData(false, 4, 4, 5)]
    [InlineData(false, 7, 4, 8)]
    [InlineData(false, 0, 4, 0)]
    public void ResolveMode_ShouldApplyNormativeRemapping(
        bool previousModeFlag,
        uint? remainderMode,
        uint predictedMode,
        uint expected)
    {
        Assert.Equal(expected, H264IntraPredictionModeReader.ResolveMode(
            previousModeFlag, remainderMode, predictedMode));
    }

    [Fact]
    public void ResolveMode_ShouldRejectRemainderWhenPreviousFlagIsSet()
    {
        Assert.Throws<ArgumentException>(() =>
            H264IntraPredictionModeReader.ResolveMode(true, 2, 4));
    }

    [Fact]
    public void ResolveMode_ShouldRejectInvalidRemainder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264IntraPredictionModeReader.ResolveMode(false, 8, 4));
    }

    [Fact]
    public void ResolveMode_ShouldRejectInvalidPredictedMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264IntraPredictionModeReader.ResolveMode(true, null, 9));
    }

    [Fact]
    public void Read_ShouldRejectTruncatedModeSyntax()
    {
        H264BitReader reader = CreateReader("0" + "101");

        Assert.Throws<EndOfStreamException>(() =>
            new H264IntraPredictionModeReader().Read(reader, true));
    }

    private static H264BitReader CreateReader(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1')
                bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
