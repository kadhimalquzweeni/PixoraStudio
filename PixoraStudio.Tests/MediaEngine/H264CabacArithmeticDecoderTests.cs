using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacArithmeticDecoderTests
{
    private static readonly byte[,] RangeLps = CreateRangeTable();
    private static readonly byte[] TransMps = Enumerable.Range(0, 64).Select(i => (byte)Math.Min(i + 1, 63)).ToArray();
    private static readonly byte[] TransLps = Enumerable.Repeat((byte)0, 64).ToArray();

    [Fact]
    public void Constructor_ShouldRequireValidTableDimensions()
    {
        Assert.Throws<ArgumentException>(() => new H264CabacArithmeticDecoder(
            new H264BitReader(new byte[2]), new byte[1, 4], TransMps, TransLps));
    }

    [Fact]
    public void Constructor_ShouldRequireNineInitialBits()
    {
        Assert.Throws<EndOfStreamException>(() => new H264CabacArithmeticDecoder(
            new H264BitReader(new byte[] { 0xFF }, 8), RangeLps, TransMps, TransLps));
    }

    [Fact]
    public void Constructor_ShouldRejectInitialOffsetOutsideInitialRange()
    {
        Assert.Throws<FormatException>(() => new H264CabacArithmeticDecoder(
            new H264BitReader(new byte[] { 0xFF, 0x80 }), RangeLps, TransMps, TransLps));
    }

    [Fact]
    public void DecodeDecision_ShouldDecodeMpsAndAdvanceContextState()
    {
        H264CabacArithmeticDecoder decoder = CreateDecoder(0);
        H264CabacContextModel context = new(10, 0);

        int bin = decoder.DecodeDecision(ref context);

        Assert.Equal(0, bin);
        Assert.Equal(11, context.State);
        Assert.Equal(0, context.MostProbableSymbol);
    }

    [Fact]
    public void DecodeDecision_ShouldDecodeLpsAndApplyStateZeroMpsSwitch()
    {
        H264CabacArithmeticDecoder decoder = CreateDecoder(509);
        H264CabacContextModel context = new(0, 0);

        int bin = decoder.DecodeDecision(ref context);

        Assert.Equal(1, bin);
        Assert.Equal(0, context.State);
        Assert.Equal(1, context.MostProbableSymbol);
    }

    [Fact]
    public void DecodeBypass_ShouldNotChangeRangeOrContext()
    {
        H264CabacArithmeticDecoder decoder = CreateDecoder(0x100);
        int range = decoder.Range;

        int bin = decoder.DecodeBypass();

        Assert.Equal(0, bin);
        Assert.Equal(range, decoder.Range);
    }

    [Fact]
    public void DecodeTerminate_ShouldReturnZeroWhenOffsetIsBelowTerminalRange()
    {
        H264CabacArithmeticDecoder decoder = CreateDecoder(0);

        Assert.Equal(0, decoder.DecodeTerminate());
        Assert.Equal(508, decoder.Range);
    }

    [Fact]
    public void DecodeTerminate_ShouldReturnOneWhenOffsetIsInTerminalInterval()
    {
        H264CabacArithmeticDecoder decoder = CreateDecoder(509);

        Assert.Equal(1, decoder.DecodeTerminate());
    }

    [Fact]
    public void DecodeDecision_ShouldRejectInvalidContextState()
    {
        H264CabacArithmeticDecoder decoder = CreateDecoder(0);
        H264CabacContextModel context = new(64, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.DecodeDecision(ref context));
    }

    private static H264CabacArithmeticDecoder CreateDecoder(int offset)
    {
        byte[] bytes = { (byte)(offset >> 1), (byte)((offset & 1) << 7) };
        return new H264CabacArithmeticDecoder(new H264BitReader(bytes, 16),
            RangeLps, TransMps, TransLps);
    }

    private static byte[,] CreateRangeTable()
    {
        byte[,] table = new byte[64, 4];
        for (int state = 0; state < 64; state++)
        for (int q = 0; q < 4; q++)
            table[state, q] = (byte)(2 + state % 20 + q);
        return table;
    }
}
