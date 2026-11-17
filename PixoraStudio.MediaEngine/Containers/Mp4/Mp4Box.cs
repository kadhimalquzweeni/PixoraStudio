
namespace PixoraStudio.MediaEngine.Containers.Mp4;

/// <summary>
/// Represents a box inside an MP4 container.
/// </summary>
public sealed class Mp4Box
{
    private readonly List<Mp4Box> _children = [];

    public ulong Size { get; }

    public string Type { get; }

    public long Offset { get; }

    public long HeaderSize { get; }

    public long DataOffset { get; }

    public long DataSize { get; }

    public bool UsesExtendedSize { get; }

    public bool ExtendsToEnd { get; }

    public IReadOnlyList<Mp4Box> Children => _children;

    public Mp4Box(
        ulong size,
        string type,
        long offset,
        long headerSize,
        bool usesExtendedSize = false,
        bool extendsToEnd = false)
    {
        if (size < (ulong)headerSize)
        {
            throw new ArgumentException(
                "Box size cannot be smaller than its header.");
        }

        if (offset < 0 || headerSize < 8)
        {
            throw new ArgumentException(
                "Invalid box offset or header size.");
        }

        Size = size;
        Type = type;
        Offset = offset;
        HeaderSize = headerSize;

        DataOffset = checked(offset + headerSize);
        DataSize = checked((long)(size - (ulong)headerSize));

        UsesExtendedSize = usesExtendedSize;
        ExtendsToEnd = extendsToEnd;
    }

    internal void AddChild(Mp4Box child)
    {
        _children.Add(child);
    }

    public override string ToString()
    {
        return $"{Type} | Size: {Size} bytes | Offset: {Offset}";
    }
}
