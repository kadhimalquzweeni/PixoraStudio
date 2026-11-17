namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents a media-data region inside an MP4 file.
/// </summary>
public sealed class Mp4MediaData
{
    public long DataOffset { get; }

    public long DataSize { get; }

    public long EndOffset => checked(DataOffset + DataSize);

    public Mp4MediaData(long dataOffset, long dataSize)
    {
        if (dataOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dataOffset),
                "Media data offset cannot be negative.");
        }

        if (dataSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dataSize),
                "Media data size cannot be negative.");
        }

        _ = checked(dataOffset + dataSize);

        DataOffset = dataOffset;
        DataSize = dataSize;
    }

    /// <summary>
    /// Determines whether a byte range is completely inside this media region.
    /// </summary>
    public bool Contains(ulong offset, uint size)
    {
        if (offset > long.MaxValue)
        {
            return false;
        }

        long sampleOffset = (long)offset;

        if (sampleOffset < DataOffset || sampleOffset > EndOffset)
        {
            return false;
        }

        return size <= EndOffset - sampleOffset;
    }
}
