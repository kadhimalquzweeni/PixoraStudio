
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models;

/// <summary>
/// Contains metadata from an MP4 visual sample entry.
/// </summary>
public sealed class Mp4VideoSampleEntry
{
    public string Format { get; }

    public ushort DataReferenceIndex { get; }

    public ushort Width { get; }

    public ushort Height { get; }

    public double HorizontalResolution { get; }

    public double VerticalResolution { get; }

    public ushort FrameCount { get; }

    public string CompressorName { get; }

    public ushort Depth { get; }

    public Mp4VideoCodec Codec { get; }

    public Mp4CodecConfiguration? CodecConfiguration { get; }

    public Mp4VideoSampleEntry(
        string format,
        ushort dataReferenceIndex,
        ushort width,
        ushort height,
        double horizontalResolution,
        double verticalResolution,
        ushort frameCount,
        string compressorName,
        ushort depth,
        Mp4VideoCodec codec,
        Mp4CodecConfiguration? codecConfiguration)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(compressorName);

        Format = format;
        DataReferenceIndex = dataReferenceIndex;
        Width = width;
        Height = height;
        HorizontalResolution = horizontalResolution;
        VerticalResolution = verticalResolution;
        FrameCount = frameCount;
        CompressorName = compressorName;
        Depth = depth;
        Codec = codec;
        CodecConfiguration = codecConfiguration;
    }
}
