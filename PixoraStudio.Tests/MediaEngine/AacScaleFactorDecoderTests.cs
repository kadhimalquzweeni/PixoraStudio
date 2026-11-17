using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacScaleFactorDecoderTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("100", -1)]
    [InlineData("1010", 1)]
    public void DecodeDelta_ShouldDecodeNormativeCodewords(string bits, int expected)
    {
        var reader = new AacBitReader(ToBytes(bits), bits.Length);
        int actual = new AacScaleFactorDecoder().DecodeDelta(reader);
        Assert.Equal(expected, actual);
        Assert.Equal(bits.Length, reader.PositionBits);
    }

    [Fact]
    public void DecodeDelta_ShouldRejectNullReader()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AacScaleFactorDecoder().DecodeDelta(null!));
    }

    private static byte[] ToBytes(string bits)
    {
        byte[] data = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1')
                data[i / 8] |= (byte)(1 << (7 - i % 8));
        return data;
    }
}
