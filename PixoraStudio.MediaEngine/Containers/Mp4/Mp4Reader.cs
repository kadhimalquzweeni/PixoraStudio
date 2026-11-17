
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Exceptions;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Reads and validates top-level boxes in an MP4 file.
/// </summary>
public sealed class Mp4Reader
{
    /// <summary>
    /// Reads all top-level boxes from an MP4 file.
    /// </summary>
    public IReadOnlyList<Mp4Box> ReadTopLevelBoxes(
        string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return ReadTopLevelBoxes(stream);
    }

    /// <summary>
    /// Reads all top-level boxes from an existing stream.
    /// The stream remains open after reading.
    /// </summary>
    public IReadOnlyList<Mp4Box> ReadTopLevelBoxes(
        Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        long originalPosition = stream.Position;
        long fileLength = stream.Length;

        List<Mp4Box> boxes = [];

        try
        {
            stream.Position = 0;

            while (stream.Position < fileLength)
            {
                long offset = stream.Position;
                long remaining = fileLength - offset;

                if (remaining < 8)
                {
                    throw new Mp4ParsingException(
                        $"Incomplete MP4 box header at offset {offset}.");
                }

                byte[] header = new byte[8];

                stream.ReadExactly(header);

                uint size32 = BinaryPrimitives.ReadUInt32BigEndian(
                    header.AsSpan(0, 4));

                string type = Encoding.ASCII.GetString(
                    header,
                    4,
                    4);

                ulong boxSize;
                long headerSize = 8;
                bool usesExtendedSize = false;
                bool extendsToEnd = false;

                // Extended 64-bit box size.
                if (size32 == 1)
                {
                    if (remaining < 16)
                    {
                        throw new Mp4ParsingException(
                            $"Incomplete extended header for box '{type}'.");
                    }

                    byte[] extendedSize = new byte[8];

                    stream.ReadExactly(extendedSize);

                    boxSize = BinaryPrimitives.ReadUInt64BigEndian(
                        extendedSize);

                    headerSize = 16;
                    usesExtendedSize = true;
                }
                // Size zero means the box extends to its parent boundary.
                else if (size32 == 0)
                {
                    boxSize = (ulong)remaining;
                    extendsToEnd = true;
                }
                else
                {
                    boxSize = size32;
                }

                // UUID boxes contain an additional 16-byte user type.
                if (type == "uuid")
                {
                    headerSize += 16;
                }

                if (boxSize < (ulong)headerSize)
                {
                    throw new Mp4ParsingException(
                        $"Invalid size for MP4 box '{type}' at offset {offset}.");
                }

                if (boxSize > (ulong)remaining)
                {
                    throw new Mp4ParsingException(
                        $"MP4 box '{type}' extends beyond the file boundary.");
                }

                if (boxSize > long.MaxValue)
                {
                    throw new Mp4ParsingException(
                        $"MP4 box '{type}' exceeds the supported file size.");
                }

                Mp4Box box;

                try
                {
                    box = new Mp4Box(
                        boxSize,
                        type,
                        offset,
                        headerSize,
                        usesExtendedSize,
                        extendsToEnd);
                }
                catch (OverflowException ex)
                {
                    throw new Mp4ParsingException(
                        $"Invalid offsets in MP4 box '{type}'.", ex);
                }

                boxes.Add(box);

                // Move to the next box.
                stream.Position = checked(
                    offset + (long)boxSize);

                // A size-zero box consumes the remaining parent.
                if (extendsToEnd)
                {
                    break;
                }
            }

            return boxes;
        }
        finally
        {
            stream.Position = originalPosition;
        }

    }

    private static readonly HashSet<string> ContainerTypes =
    [
        "moov",
        "trak",
        "mdia",
        "minf",
        "stbl",
        "edts",
        "dinf",
        "mvex",
        "moof",
        "traf",
        "mfra"
    ];

    private const int MaximumNestingDepth = 32;

    /// <summary>
    /// Reads an MP4 file and recursively parses recognised container boxes.
    /// </summary>
    public IReadOnlyList<Mp4Box> ReadBoxTree(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return ReadBoxTree(stream);
    }

    /// <summary>
    /// Reads an MP4 stream and constructs its box hierarchy.
    /// The original stream position is restored afterwards.
    /// </summary>
    public IReadOnlyList<Mp4Box> ReadBoxTree(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must support reading and seeking.");
        }

        long originalPosition = stream.Position;

        try
        {
            IReadOnlyList<Mp4Box> boxes =
                ReadTopLevelBoxes(stream);

            foreach (Mp4Box box in boxes)
            {
                ReadChildren(stream, box, 0);
            }

            return boxes;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private void ReadChildren(
        Stream stream,
        Mp4Box parent,
        int depth)
    {
        if (!ContainerTypes.Contains(parent.Type))
            return;

        if (depth >= MaximumNestingDepth)
        {
            throw new Mp4ParsingException(
                "Maximum MP4 box nesting depth exceeded.");
        }

        long parentEnd = checked(
            parent.Offset + (long)parent.Size);

        long position = parent.DataOffset;

        while (position < parentEnd)
        {
            Mp4Box child = ReadBox(
                stream,
                position,
                parentEnd);

            parent.AddChild(child);

            ReadChildren(
                stream,
                child,
                depth + 1);

            position = checked(
                child.Offset + (long)child.Size);
        }

        if (position != parentEnd)
        {
            throw new Mp4ParsingException(
                $"Invalid child boundaries inside '{parent.Type}'.");
        }
    }

    private static Mp4Box ReadBox(
        Stream stream,
        long offset,
        long boundary)
    {
        long remaining = boundary - offset;

        if (remaining < 8)
        {
            throw new Mp4ParsingException(
                $"Incomplete MP4 box header at offset {offset}.");
        }

        stream.Position = offset;

        byte[] header = new byte[8];

        stream.ReadExactly(header);

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(
            header.AsSpan(0, 4));

        string type = Encoding.ASCII.GetString(
            header,
            4,
            4);

        ulong boxSize;
        long headerSize = 8;

        bool extended = false;
        bool extendsToEnd = false;

        if (size32 == 1)
        {
            if (remaining < 16)
            {
                throw new Mp4ParsingException(
                    $"Incomplete extended header for '{type}'.");
            }

            byte[] extendedHeader = new byte[8];

            stream.ReadExactly(extendedHeader);

            boxSize = BinaryPrimitives.ReadUInt64BigEndian(
                extendedHeader);

            headerSize = 16;
            extended = true;
        }
        else if (size32 == 0)
        {
            boxSize = (ulong)remaining;
            extendsToEnd = true;
        }
        else
        {
            boxSize = size32;
        }

        if (type == "uuid")
        {
            headerSize += 16;
        }

        if (boxSize < (ulong)headerSize)
        {
            throw new Mp4ParsingException(
                $"Invalid size for box '{type}' at offset {offset}.");
        }

        if (boxSize > (ulong)remaining ||
            boxSize > long.MaxValue)
        {
            throw new Mp4ParsingException(
                $"Box '{type}' exceeds its parent boundary.");
        }

        try
        {
            return new Mp4Box(
                boxSize,
                type,
                offset,
                headerSize,
                extended,
                extendsToEnd);
        }
        catch (OverflowException ex)
        {
            throw new Mp4ParsingException(
                $"Invalid box offsets for '{type}'.", ex);
        }
    }

}