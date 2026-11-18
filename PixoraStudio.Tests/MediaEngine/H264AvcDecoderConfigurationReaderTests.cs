using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264AvcDecoderConfigurationReaderTests
{
    [Fact]
    public void Read_ShouldExtractSpsPpsAndFourByteNaluLengths()
    {
        byte[] data = CreateBasicRecord(lengthSizeMinusOne: 3);
        H264AvcDecoderConfiguration result =
            new H264AvcDecoderConfigurationReader().Read(data);

        Assert.Equal((byte)66, result.Profile);
        Assert.Equal((byte)0, result.Compatibility);
        Assert.Equal((byte)30, result.Level);
        Assert.Equal(4, result.NaluLengthSize);
        Assert.Equal((byte)7, Assert.Single(result.SequenceParameterSets).NalUnitType);
        Assert.Equal((byte)8, Assert.Single(result.PictureParameterSets).NalUnitType);
        Assert.Empty(result.SequenceParameterSetExtensions);
        Assert.Null(result.ChromaFormat);
    }

    [Fact]
    public void Read_ShouldAcceptMp4CodecConfigurationModel()
    {
        Mp4CodecConfiguration configuration = new(
            Mp4VideoCodec.Avc, "avcC",
            CreateBasicRecord(lengthSizeMinusOne: 0),
            configurationVersion: 1, profile: 66, compatibility: 0, level: 30,
            naluLengthSize: 1);

        H264AvcDecoderConfiguration result =
            new H264AvcDecoderConfigurationReader().Read(configuration);

        Assert.Equal(1, result.NaluLengthSize);
        Assert.Single(result.SequenceParameterSets);
        Assert.Single(result.PictureParameterSets);
    }

    [Fact]
    public void Read_ShouldParseHighProfileExtensionAndSpsExtensionNals()
    {
        List<byte> data = CreateBasicRecord(lengthSizeMinusOne: 1, profile: 100).ToList();
        data.Add(0xFD); // reserved six bits + chroma_format_idc 1
        data.Add(0xFA); // reserved five bits + bit_depth_luma_minus8 2
        data.Add(0xF9); // reserved five bits + bit_depth_chroma_minus8 1
        data.Add(1);    // numOfSequenceParameterSetExt
        AddNal(data, 0x6D, 0x80); // type 13

        H264AvcDecoderConfiguration result =
            new H264AvcDecoderConfigurationReader().Read(data.ToArray());

        Assert.Equal((byte?)1, result.ChromaFormat);
        Assert.Equal((byte?)2, result.BitDepthLumaMinus8);
        Assert.Equal((byte?)1, result.BitDepthChromaMinus8);
        Assert.Equal((byte)13, Assert.Single(result.SequenceParameterSetExtensions).NalUnitType);
        Assert.Equal(2, result.NaluLengthSize);
    }

    [Theory]
    [InlineData(0, 0, "version")]
    [InlineData(4, 0, "length-size reserved bits")]
    [InlineData(5, 0, "SPS-count reserved bits")]
    public void Read_ShouldRejectInvalidFixedHeaderFields(int index, byte value, string _)
    {
        byte[] data = CreateBasicRecord(lengthSizeMinusOne: 3);
        data[index] = value;

        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data));
    }

    [Fact]
    public void Read_ShouldRejectReservedThreeByteNaluLengthSize()
    {
        byte[] data = CreateBasicRecord(lengthSizeMinusOne: 2);
        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data));
    }

    [Fact]
    public void Read_ShouldRejectWrongParameterSetNalTypes()
    {
        byte[] data = CreateBasicRecord(lengthSizeMinusOne: 3);
        data[8] = 0x68; // SPS array must contain type 7.
        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data));

        data = CreateBasicRecord(lengthSizeMinusOne: 3);
        data[13] = 0x67; // PPS array must contain type 8.
        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data));
    }

    [Fact]
    public void Read_ShouldRejectUnescapedEmulationSequenceInsideAvccNal()
    {
        byte[] data =
        [
            1, 66, 0, 30, 0xFF, 0xE1,
            0, 4, 0x67, 0, 0, 1,
            1, 0, 2, 0x68, 0x80
        ];

        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data));
    }

    [Fact]
    public void Read_ShouldRejectParameterSetNalsWithZeroReferencePriority()
    {
        byte[] data = CreateBasicRecord(lengthSizeMinusOne: 3);
        data[8] = 0x07; // SPS NAL with nal_ref_idc == 0.

        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data));

        data = CreateBasicRecord(lengthSizeMinusOne: 3);
        data[13] = 0x08; // PPS NAL with nal_ref_idc == 0.

        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data));
    }

    [Fact]
    public void Read_ShouldRejectTruncatedOrEmptyNalUnits()
    {
        byte[] truncated = CreateBasicRecord(lengthSizeMinusOne: 3);
        truncated[7] = 0x7F;
        truncated[8] = 0xFF;
        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(truncated));

        byte[] empty = CreateBasicRecord(lengthSizeMinusOne: 3);
        empty[6] = 0;
        empty[7] = 0;
        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(empty));
    }

    [Fact]
    public void Read_ShouldRejectMalformedHighProfileReservedBitsAndUnexpectedTrailingData()
    {
        List<byte> data = CreateBasicRecord(lengthSizeMinusOne: 3, profile: 100).ToList();
        data.AddRange([0x00, 0xFD, 0xF8, 0xF8, 0x00]);
        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data.ToArray()));

        data = CreateBasicRecord(lengthSizeMinusOne: 3).ToList();
        data.Add(0);
        Assert.Throws<FormatException>(() =>
            new H264AvcDecoderConfigurationReader().Read(data.ToArray()));
    }

    [Fact]
    public void Read_ShouldRejectNonAvcCodecConfiguration()
    {
        Mp4CodecConfiguration configuration = new(
            Mp4VideoCodec.Hevc, "hvcC", new byte[23], 1);

        Assert.Throws<ArgumentException>(() =>
            new H264AvcDecoderConfigurationReader().Read(configuration));
    }

    private static byte[] CreateBasicRecord(int lengthSizeMinusOne, byte profile = 66)
    {
        List<byte> data =
        [
            1, profile, 0, 30,
            (byte)(0xFC | lengthSizeMinusOne),
            0xE1
        ];
        AddNal(data, 0x67, 0x80);
        data.Add(1); // numOfPictureParameterSets
        AddNal(data, 0x68, 0x80);
        return data.ToArray();
    }

    private static void AddNal(List<byte> data, byte header, byte payload)
    {
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, 2);
        data.Add(length[0]);
        data.Add(length[1]);
        data.Add(header);
        data.Add(payload);
    }
}
