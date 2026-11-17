
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models;

/// <summary>
/// Contains codec configuration extracted from an MP4 sample entry.
/// </summary>
public sealed class Mp4CodecConfiguration
{
    private readonly byte[] _data;

    public Mp4VideoCodec Codec { get; }

    public string BoxType { get; }

    public ReadOnlyMemory<byte> Data => _data;

    public byte ConfigurationVersion { get; }

    public byte Profile { get; }

    public byte Compatibility { get; }

    public byte Level { get; }

    public int NaluLengthSize { get; }

    public Mp4CodecConfiguration(
        Mp4VideoCodec codec,
        string boxType,
        byte[] data,
        byte configurationVersion,
        byte profile = 0,
        byte compatibility = 0,
        byte level = 0,
        int naluLengthSize = 0)
    {
        ArgumentNullException.ThrowIfNull(boxType);
        ArgumentNullException.ThrowIfNull(data);

        Codec = codec;
        BoxType = boxType;
        _data = data.ToArray();
        ConfigurationVersion = configurationVersion;
        Profile = profile;
        Compatibility = compatibility;
        Level = level;
        NaluLengthSize = naluLengthSize;
    }
}
