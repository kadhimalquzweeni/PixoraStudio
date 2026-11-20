using PixoraStudio.MediaEngine.Containers.Mp4;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Rewrites a parsed MP4 box tree while copying unchanged leaf boxes byte-for-byte.
/// A replacement callback can emit a complete replacement box and return true.
/// </summary>
public sealed class Mp4BoxTreeWriter
{
    private readonly Mp4BoxWriter _boxWriter = new();

    public void Write(
        Stream source,
        Stream destination,
        IEnumerable<Mp4Box> boxes,
        Func<Mp4Box, Stream, bool>? replaceBox = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(boxes);

        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The source must be readable and seekable.", nameof(source));
        if (!destination.CanWrite)
            throw new ArgumentException("The destination must be writable.", nameof(destination));
        if (ReferenceEquals(source, destination))
            throw new ArgumentException("Source and destination streams must be different.", nameof(destination));

        foreach (Mp4Box box in boxes)
            WriteBox(source, destination, box, replaceBox);
    }

    private void WriteBox(
        Stream source,
        Stream destination,
        Mp4Box box,
        Func<Mp4Box, Stream, bool>? replaceBox)
    {
        if (replaceBox is not null && replaceBox(box, destination))
            return;

        if (box.Children.Count > 0)
        {
            _boxWriter.WriteBox(destination, box.Type, payload =>
            {
                foreach (Mp4Box child in box.Children)
                    WriteBox(source, payload, child, replaceBox);
            });

            return;
        }

        if (box.Offset < 0 || box.Size > long.MaxValue ||
            box.Offset > source.Length ||
            (long)box.Size > source.Length - box.Offset)
        {
            throw new InvalidDataException($"Box '{box.Type}' extends beyond the source stream.");
        }

        source.Position = box.Offset;
        CopyExactly(source, destination, checked((long)box.Size));
    }

    private static void CopyExactly(Stream source, Stream destination, long length)
    {
        byte[] buffer = new byte[64 * 1024];
        long remaining = length;

        while (remaining > 0)
        {
            int count = (int)Math.Min(buffer.Length, remaining);
            source.ReadExactly(buffer.AsSpan(0, count));
            destination.Write(buffer, 0, count);
            remaining -= count;
        }
    }
}
