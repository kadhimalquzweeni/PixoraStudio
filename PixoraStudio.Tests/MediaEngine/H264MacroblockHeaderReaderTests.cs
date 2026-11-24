using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264MacroblockHeaderReaderTests
{
    [Theory]
    [InlineData(0, true, false, null, null, null, false)]
    [InlineData(1, true, true, 0, 0, 0, false)]
    [InlineData(4, true, true, 3, 0, 0, false)]
    [InlineData(13, true, true, 0, 15, 0, false)]
    [InlineData(24, true, true, 3, 15, 2, false)]
    [InlineData(25, true, false, null, null, null, true)]
    public void ReadNext_IType_ShouldDecodeIntraMacroblockType(
        uint mbType,
        bool isIntra,
        bool isIntra16x16,
        int? predMode,
        int? cbpLuma,
        int? cbpChroma,
        bool isPcm)
    {
        H264BitReader bits = CreateReader(WriteUe(mbType));

        H264MacroblockHeader result = new H264MacroblockHeaderReader().ReadNext(
            bits, H264SliceType.I, 0, 1);

        Assert.Equal(mbType, result.MbTypeCode);
        Assert.Equal(isIntra, result.IsIntra);
        Assert.Equal(isIntra16x16, result.IsIntra16x16);
        Assert.Equal(predMode is null ? null : (uint?)predMode, result.Intra16x16PredMode);
        Assert.Equal(cbpLuma is null ? null : (uint?)cbpLuma, result.CodedBlockPatternLuma);
        Assert.Equal(cbpChroma is null ? null : (uint?)cbpChroma, result.CodedBlockPatternChroma);
        Assert.Equal(isPcm, result.IsPcm);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(30, true)]
    public void ReadNext_PType_ShouldDistinguishInterAndIntraTypes(uint mbType, bool expectedIntra)
    {
        H264BitReader bits = CreateReader(WriteUe(0) + WriteUe(mbType));

        H264MacroblockHeader result = new H264MacroblockHeaderReader().ReadNext(
            bits, H264SliceType.P, 0, 1);

        Assert.Equal(expectedIntra, result.IsIntra);
        Assert.Equal(mbType, result.MbTypeCode);
        if (mbType <= 4)
            Assert.False(result.IsIntra16x16);
    }

    [Fact]
    public void ReadNext_PSlice_ShouldReturnSkipRunWithoutReadingFollowingMacroblockType()
    {
        H264BitReader bits = CreateReader(WriteUe(3) + WriteUe(1));

        H264MacroblockHeader result = new H264MacroblockHeaderReader().ReadNext(
            bits, H264SliceType.P, 4, 8);

        Assert.True(result.IsSkipped);
        Assert.Equal(3u, result.SkippedMacroblockCount);
        Assert.Equal(4u, result.MacroblockAddress);
        Assert.Equal(WriteUe(1).Length, bits.RemainingBits);
    }

    [Fact]
    public void ReadNext_PSlice_ShouldReadMacroblockTypeWhenSkipRunIsZero()
    {
        H264BitReader bits = CreateReader(WriteUe(0) + WriteUe(5));

        H264MacroblockHeader result = new H264MacroblockHeaderReader().ReadNext(
            bits, H264SliceType.P, 2, 4);

        Assert.True(result.IsIntra);
        Assert.Equal(5u, result.MbTypeCode);
        Assert.Null(result.Intra16x16PredMode);
    }

    [Fact]
    public void ReadNext_ShouldRejectUnsupportedSliceTypes()
    {
        Assert.Throws<NotSupportedException>(() =>
            new H264MacroblockHeaderReader().ReadNext(
                CreateReader("1"), H264SliceType.B, 0, 1));
    }

    [Fact]
    public void ReadNext_ShouldRejectCabac()
    {
        Assert.Throws<NotSupportedException>(() =>
            new H264MacroblockHeaderReader().ReadNext(
                CreateReader("1"), H264SliceType.I, 0, 1, true));
    }

    [Fact]
    public void ReadNext_ShouldRejectSkipRunBeyondPicture()
    {
        Assert.Throws<FormatException>(() =>
            new H264MacroblockHeaderReader().ReadNext(
                CreateReader(WriteUe(3)), H264SliceType.P, 1, 3));
    }

    [Fact]
    public void ReadNext_ShouldRejectInvalidMacroblockAddress()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new H264MacroblockHeaderReader().ReadNext(
                CreateReader("1"), H264SliceType.I, 1, 1));
    }

    private static string WriteUe(uint value)
    {
        uint codeNum = checked(value + 1);
        int leadingZeroes = 0;
        for (uint current = codeNum; current > 1; current >>= 1)
            leadingZeroes++;

        string suffix = Convert.ToString(codeNum, 2).PadLeft(leadingZeroes + 1, '0');
        return new string('0', leadingZeroes) + suffix;
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
