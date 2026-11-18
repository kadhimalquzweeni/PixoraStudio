using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264RbspTests
{
    [Fact]
    public void FromEbsp_ShouldRemoveEmulationPreventionBytes()
    {
        byte[] ebsp = [0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x03, 0x03];
        byte[] expected = [0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x03];
        Assert.Equal(expected, H264Rbsp.FromEbsp(ebsp));
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x03 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x03, 0x04 })]
    public void FromEbsp_ShouldRejectInvalidPreventionByte(byte[] ebsp)
    {
        Assert.Throws<FormatException>(() => H264Rbsp.FromEbsp(ebsp));
    }

    [Fact]
    public void FromEbsp_ShouldReturnEmptyForEmptyPayload()
    {
        Assert.Empty(H264Rbsp.FromEbsp([]));
    }
}
