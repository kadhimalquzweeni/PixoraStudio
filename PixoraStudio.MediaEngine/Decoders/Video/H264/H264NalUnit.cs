namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>One H.264 network abstraction layer unit, excluding its framing prefix.</summary>
public sealed class H264NalUnit
{
    private readonly byte[] _payload;

    public byte Header { get; }
    public byte NalRefIdc => (byte)((Header >> 5) & 0x03);
    public byte NalUnitType => (byte)(Header & 0x1F);
    public ReadOnlyMemory<byte> Payload => _payload;
    public ReadOnlyMemory<byte> Rbsp => H264Rbsp.FromEbsp(_payload);

    public H264NalUnit(byte header, ReadOnlySpan<byte> payload)
    {
        if ((header & 0x80) != 0)
            throw new ArgumentException("H.264 forbidden_zero_bit must be zero.", nameof(header));
        byte type = (byte)(header & 0x1F);
        if (type is 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(header), "The NAL unit type is reserved or unspecified.");
        Header = header;
        _payload = payload.ToArray();
    }
}
