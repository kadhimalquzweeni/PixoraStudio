namespace PixoraStudio.MediaEngine.Models.General;

/// <summary>
/// Represents an encoded MP4 sample and its timing information.
/// </summary>
public sealed class Mp4EncodedSample
{
    private readonly byte[] _data;

    public Mp4SampleLocation Location { get; }

    public ReadOnlyMemory<byte> Data => _data;

    public Mp4EncodedSample(
        Mp4SampleLocation location,
        byte[] data)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(data);

        Location = location;
        _data = data.ToArray();
    }
}
