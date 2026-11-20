using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Rewrites the duration field in mvhd, tkhd, or mdhd while preserving the rest
/// of the original box payload and header.
/// </summary>
public sealed class Mp4DurationBoxWriter
{
    public void Write(Stream source, Stream destination, Mp4Box box, ulong durationTicks)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(box);

        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The source must be readable and seekable.", nameof(source));
        if (!destination.CanWrite)
            throw new ArgumentException("The destination must be writable.", nameof(destination));

        int durationOffset;
        int requiredPayloadSize;
        bool versionOne;

        byte[] versionBuffer = new byte[1];
        source.Position = box.DataOffset;
        source.ReadExactly(versionBuffer);

        switch (box.Type, versionBuffer[0])
        {
            case ("mvhd", 0):
            case ("mdhd", 0):
                durationOffset = 16;
                requiredPayloadSize = 20;
                versionOne = false;
                break;
            case ("mvhd", 1):
            case ("mdhd", 1):
                durationOffset = 24;
                requiredPayloadSize = 32;
                versionOne = true;
                break;
            case ("tkhd", 0):
                durationOffset = 20;
                requiredPayloadSize = 84;
                versionOne = false;
                break;
            case ("tkhd", 1):
                durationOffset = 28;
                requiredPayloadSize = 96;
                versionOne = true;
                break;
            whenUnsupported:
                throw new Mp4ParsingException($"Unsupported duration box '{box.Type}' version {versionBuffer[0]}.");
        }

        if (box.DataSize < requiredPayloadSize)
            throw new Mp4ParsingException($"The {box.Type} box is too small for its version.");

        if (!versionOne && durationTicks > uint.MaxValue)
            throw new NotSupportedException($"The {box.Type} version 0 duration cannot represent {durationTicks} ticks.");

        if (box.Size > int.MaxValue)
            throw new NotSupportedException("The duration box exceeds the supported in-memory size.");

        byte[] bytes = new byte[checked((int)box.Size)];
        source.Position = box.Offset;
        source.ReadExactly(bytes);

        Span<byte> durationField = bytes.AsSpan(
            checked((int)box.HeaderSize + durationOffset),
            versionOne ? 8 : 4);

        if (versionOne)
            BinaryPrimitives.WriteUInt64BigEndian(durationField, durationTicks);
        else
            BinaryPrimitives.WriteUInt32BigEndian(durationField, (uint)durationTicks);

        destination.Write(bytes);
    }
}
