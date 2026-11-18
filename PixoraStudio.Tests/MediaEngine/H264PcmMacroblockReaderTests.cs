using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264PcmMacroblockReaderTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void Read_ShouldParseByteAlignedLumaAndChromaSamples(uint chromaFormat, int chromaHeight)
    {
        byte[] luma = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        byte[] cb = Enumerable.Range(0, 8 * chromaHeight).Select(value => (byte)(value + 3)).ToArray();
        byte[] cr = Enumerable.Range(0, 8 * chromaHeight).Select(value => (byte)(value + 7)).ToArray();
        H264BitReader reader = CreateReaderWithThreeBitPrefix(luma.Concat(cb).Concat(cr).ToArray());
        reader.SkipBits(3);

        H264PcmMacroblock result = new H264PcmMacroblockReader().Read(
            reader, CreateHeader(5), CreateSps(chromaFormat), CreatePps());

        Assert.Equal((uint)5, result.MacroblockAddress);
        Assert.Equal(luma, result.LumaSamples);
        Assert.Equal(cb, result.CbSamples);
        Assert.Equal(cr, result.CrSamples);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void Read_ShouldRejectNonZeroPcmAlignmentBit()
    {
        H264BitReader reader = CreateReader("101" + "00001" + new string('0', (256 + 128) * 8));
        reader.SkipBits(3);

        Assert.Throws<FormatException>(() =>
            new H264PcmMacroblockReader().Read(
                reader, CreateHeader(0), CreateSps(1), CreatePps()));
    }

    [Fact]
    public void Read_ShouldRejectNonPcmHeader()
    {
        Assert.Throws<ArgumentException>(() =>
            new H264PcmMacroblockReader().Read(
                CreateReader(new string('0', (256 + 128) * 8)),
                CreateHeader(0) with { IsPcm = false },
                CreateSps(1), CreatePps()));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedBitDepthAndAllowCabacPcm()
    {
        H264PcmMacroblockReader reader = new();
        Assert.Throws<NotSupportedException>(() =>
            reader.Read(CreateReader(new string('0', (256 + 128) * 8)),
                CreateHeader(0), CreateSps(1) with { BitDepthLumaMinus8 = 1 }, CreatePps()));
        H264PcmMacroblock cabacResult = reader.Read(
            CreateReader(new string('0', (256 + 128) * 8)),
            CreateHeader(0), CreateSps(1), CreatePps() with { EntropyCodingModeFlag = true });
        Assert.Equal(256, cabacResult.LumaSamples.Count);
        Assert.Equal(64, cabacResult.CbSamples.Count);
        Assert.Equal(64, cabacResult.CrSamples.Count);
    }

    [Fact]
    public void Read_ShouldRejectTruncatedSamplePayload()
    {
        Assert.Throws<EndOfStreamException>(() =>
            new H264PcmMacroblockReader().Read(
                CreateReader(new string('0', 8 * 20)),
                CreateHeader(0), CreateSps(1), CreatePps()));
    }

    private static H264MacroblockHeader CreateHeader(uint address) =>
        new(address, 25, false, 0, true, false, null, null, null, true);

    private static H264SequenceParameterSet CreateSps(uint chromaFormat) =>
        new(0, 66, 30, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            1, 1, true, true, 32, 32, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);

    private static H264BitReader CreateReaderWithThreeBitPrefix(IReadOnlyList<byte> samples)
    {
        string bits = "101" + "00000" +
            string.Concat(samples.Select(value => Convert.ToString(value, 2).PadLeft(8, '0')));
        return CreateReader(bits);
    }

    private static H264BitReader CreateReader(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1') bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
