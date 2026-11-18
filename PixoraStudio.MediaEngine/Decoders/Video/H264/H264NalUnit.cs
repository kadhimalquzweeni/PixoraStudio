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

    internal H264NalUnit(byte header, ReadOnlySpan<byte> payload)
    {
        Header = header;
        _payload = payload.ToArray();
    }
}
