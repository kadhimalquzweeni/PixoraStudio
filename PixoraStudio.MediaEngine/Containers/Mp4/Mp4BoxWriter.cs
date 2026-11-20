using System.Buffers.Binary;
using System.Text;

namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Writes ISO Base Media File Format boxes using managed streams.
/// </summary>
public sealed class Mp4BoxWriter
{
    public void WriteBox(Stream destination, string type, Action<Stream> writePayload)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(writePayload);

        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));

        if (type is null || type.Length != 4 || type.Any(character => character > 0x7F))
            throw new ArgumentException("An MP4 box type must contain exactly four ASCII characters.", nameof(type));

        using MemoryStream payload = new();
        writePayload(payload);

        ulong boxSize = checked((ulong)payload.Length + 8);
        Span<byte> header = stackalloc byte[8];

        if (boxSize <= uint.MaxValue)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)boxSize);
            Encoding.ASCII.GetBytes(type, header[4..]);
            destination.Write(header);
        }
        else
        {
            // Rebuild using a 64-bit extended-size header.
            Span<byte> extendedHeader = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(extendedHeader[..4], 1);
            Encoding.ASCII.GetBytes(type, extendedHeader[4..8]);
            BinaryPrimitives.WriteUInt64BigEndian(extendedHeader[8..], checked(boxSize + 8));
            destination.Write(extendedHeader);
        }

        payload.Position = 0;
        payload.CopyTo(destination);
    }

    public void WriteFullBox(
        Stream destination,
        string type,
        byte version,
        uint flags,
        Action<Stream> writeBody)
    {
        ArgumentNullException.ThrowIfNull(writeBody);

        WriteBox(destination, type, payload =>
        {
            payload.WriteByte(version);
            Span<byte> flagBytes = stackalloc byte[3];
            flagBytes[0] = (byte)(flags >> 16);
            flagBytes[1] = (byte)(flags >> 8);
            flagBytes[2] = (byte)flags;
            payload.Write(flagBytes);
            writeBody(payload);
        });
    }
}
