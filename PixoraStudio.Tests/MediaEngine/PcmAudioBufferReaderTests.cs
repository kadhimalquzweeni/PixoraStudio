using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class PcmAudioBufferReaderTests
{
    [Fact]
    public void Read_ShouldConvertInterleavedSamplesToPcm16()
    {
        PcmAudioBuffer buffer = new(
            new PcmAudioFormat(48000, 2),
            [0f, 1f, -1f, 0.5f]);

        PcmAudioBufferReader reader = new(buffer);
        byte[] output = new byte[8];

        int bytesRead = reader.Read(output, 0, output.Length);

        Assert.Equal(8, bytesRead);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(0, 2)));
        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(2, 2)));
        Assert.Equal(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(4, 2)));
        Assert.Equal(16384, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(6, 2)));
    }

    [Fact]
    public void Read_ShouldClampSamplesOutsideNormalizedRange()
    {
        PcmAudioBuffer buffer = new(
            new PcmAudioFormat(44100, 1),
            [2f, -2f]);

        PcmAudioBufferReader reader = new(buffer);
        byte[] output = new byte[4];

        reader.Read(output, 0, output.Length);

        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(0, 2)));
        Assert.Equal(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(2, 2)));
    }

    [Fact]
    public void Read_ShouldRespectDestinationOffset()
    {
        PcmAudioBuffer buffer = new(
            new PcmAudioFormat(44100, 1),
            [1f]);

        PcmAudioBufferReader reader = new(buffer);
        byte[] output = [0xAA, 0xAA, 0xAA, 0xAA];

        int bytesRead = reader.Read(output, 1, 2);

        Assert.Equal(2, bytesRead);
        Assert.Equal(0xAA, output[0]);
        Assert.Equal(0xFF, output[1]);
        Assert.Equal(0x7F, output[2]);
        Assert.Equal(0xAA, output[3]);
    }

    [Fact]
    public void Read_ShouldReturnOnlyCompleteFrames()
    {
        PcmAudioBuffer buffer = new(
            new PcmAudioFormat(44100, 2),
            [0.1f, 0.2f, 0.3f, 0.4f]);

        PcmAudioBufferReader reader = new(buffer);
        byte[] output = new byte[7];

        int bytesRead = reader.Read(output, 0, output.Length);

        Assert.Equal(4, bytesRead);
        Assert.Equal(TimeSpan.FromSeconds(1d / 44100), reader.Position);
    }

    [Fact]
    public void Read_ShouldReturnZeroAfterEndOfBuffer()
    {
        PcmAudioBuffer buffer = new(
            new PcmAudioFormat(44100, 1),
            [0.25f]);

        PcmAudioBufferReader reader = new(buffer);
        byte[] output = new byte[4];

        Assert.Equal(2, reader.Read(output, 0, output.Length));
        Assert.Equal(0, reader.Read(output, 0, output.Length));
        Assert.Equal(2, reader.PositionBytes);
    }

    [Fact]
    public void Seek_ShouldMoveToRequestedFrame()
    {
        PcmAudioBuffer buffer = new(
            new PcmAudioFormat(10, 1),
            [0f, 0.1f, 0.2f, 0.3f]);

        PcmAudioBufferReader reader = new(buffer);

        reader.Seek(TimeSpan.FromMilliseconds(200));

        Assert.Equal(TimeSpan.FromMilliseconds(200), reader.Position);
        Assert.Equal(4, reader.PositionBytes);

        byte[] output = new byte[2];
        Assert.Equal(2, reader.Read(output, 0, output.Length));
        Assert.Equal(6553, BinaryPrimitives.ReadInt16LittleEndian(output));
    }

    [Fact]
    public void SeekToFrame_ShouldPositionExactlyAtRequestedFrame()
    {
        PcmAudioBufferReader reader = new(
            new PcmAudioBuffer(
                new PcmAudioFormat(44100, 1),
                [0f, 0.1f, 0.2f]));

        reader.SeekToFrame(1);

        Assert.Equal(1, reader.PositionBytes / reader.BytesPerFrame);

        byte[] output = new byte[2];
        Assert.Equal(2, reader.Read(output, 0, output.Length));
        Assert.Equal(3277, BinaryPrimitives.ReadInt16LittleEndian(output));
    }

    [Fact]
    public void SeekToFrame_ShouldRejectNegativeFrame()
    {
        PcmAudioBufferReader reader = new(
            new PcmAudioBuffer(new PcmAudioFormat(44100, 1), [0f]));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.SeekToFrame(-1));
    }

    [Fact]
    public void SeekToFrame_ShouldClampPastEnd()
    {
        PcmAudioBufferReader reader = new(
            new PcmAudioBuffer(new PcmAudioFormat(44100, 1), [0f]));

        reader.SeekToFrame(10);

        Assert.Equal(reader.LengthBytes, reader.PositionBytes);
    }

    [Fact]
    public void Seek_ShouldClampPastEndToEndOfBuffer()
    {
        PcmAudioBuffer buffer = new(
            new PcmAudioFormat(10, 1),
            [0f, 0.1f]);

        PcmAudioBufferReader reader = new(buffer);

        reader.Seek(TimeSpan.FromSeconds(10));

        Assert.Equal(0, reader.Read(new byte[2], 0, 2));
        Assert.Equal(TimeSpan.FromMilliseconds(200), reader.Position);
    }

    [Fact]
    public void Seek_ShouldRejectNegativePosition()
    {
        PcmAudioBufferReader reader = new(
            new PcmAudioBuffer(new PcmAudioFormat(10, 1), [0f]));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.Seek(TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void Read_ShouldValidateDestinationArguments()
    {
        PcmAudioBufferReader reader = new(
            new PcmAudioBuffer(new PcmAudioFormat(10, 1), [0f]));

        Assert.Throws<ArgumentNullException>(
            () => reader.Read(null!, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.Read(new byte[2], -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.Read(new byte[2], 0, 3));
    }
}
