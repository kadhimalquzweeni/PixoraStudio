
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads sample sizes from an MP4 stsz box.
/// </summary>
public sealed class Mp4SampleSizeReader
{
    private const uint MaximumSampleCount = 10_000_000;

    public Mp4SampleSizeTable Read(
        Stream stream,
        Mp4Box box)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(box);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        if (box.Type != "stsz")
        {
            throw new Mp4ParsingException(
                "Expected an stsz box.");
        }

        long originalPosition = stream.Position;

        try
        {
            if (box.DataOffset < 0 ||
                box.DataOffset > stream.Length ||
                box.DataSize > stream.Length - box.DataOffset)
            {
                throw new Mp4ParsingException(
                    "The stsz box extends beyond the stream.");
            }

            if (box.DataSize < 12)
            {
                throw new Mp4ParsingException(
                    "The stsz box is too small.");
            }

            stream.Position = box.DataOffset;

            byte[] header = new byte[12];
            stream.ReadExactly(header);

            uint versionAndFlags =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(0, 4));

            if ((versionAndFlags >> 24) != 0)
            {
                throw new Mp4ParsingException(
                    "Unsupported stsz version.");
            }

            uint defaultSampleSize =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(4, 4));

            uint sampleCount =
                BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(8, 4));

            if (sampleCount > MaximumSampleCount)
            {
                throw new Mp4ParsingException(
                    "The stsz sample count exceeds the supported limit.");
            }

            if (defaultSampleSize != 0)
            {
                if (box.DataSize != 12)
                {
                    throw new Mp4ParsingException(
                        "An stsz box with a default sample size must not contain individual sizes.");
                }

                return new Mp4SampleSizeTable(
                    defaultSampleSize,
                    sampleCount,
                    []);
            }

            long expectedDataSize = 12L + (sampleCount * 4L);

            if (expectedDataSize != box.DataSize)
            {
                throw new Mp4ParsingException(
                    "The stsz sample count does not match its box size.");
            }

            List<uint> sampleSizes = new(
                checked((int)sampleCount));

            byte[] sizeData = new byte[4];

            for (uint i = 0; i < sampleCount; i++)
            {
                stream.ReadExactly(sizeData);

                uint sampleSize =
                    BinaryPrimitives.ReadUInt32BigEndian(
                        sizeData);

                sampleSizes.Add(sampleSize);
            }

            return new Mp4SampleSizeTable(
                0,
                sampleCount,
                sampleSizes);
        }
        catch (EndOfStreamException ex)
        {
            throw new Mp4ParsingException(
                "Unexpected end of stream while reading stsz.", ex);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                "The stsz sample count is too large.", ex);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
