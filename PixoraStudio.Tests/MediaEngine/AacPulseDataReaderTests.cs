using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacPulseDataReaderTests
{
    private readonly AacPulseDataReader _reader = new();

    [Fact]
    public void Read_ShouldParsePulseCountStartBandOffsetsAndAmplitudes()
    {
        var bits = new BitWriter();
        bits.Write(2, 2);  // three pulses
        bits.Write(12, 6); // start SFB
        bits.Write(3, 5); bits.Write(7, 4);
        bits.Write(2, 5); bits.Write(4, 4);
        bits.Write(5, 5); bits.Write(9, 4);

        var result = _reader.Read(
            new AacBitReader(bits.ToArray(), bits.Length),
            LongWindowInfo());

        Assert.Equal(12, result.PulseStartScaleFactorBand);
        Assert.Collection(result.Pulses,
            pulse => { Assert.Equal(3, pulse.Offset); Assert.Equal(7, pulse.Amplitude); },
            pulse => { Assert.Equal(5, pulse.Offset); Assert.Equal(4, pulse.Amplitude); },
            pulse => { Assert.Equal(10, pulse.Offset); Assert.Equal(9, pulse.Amplitude); });
    }

    [Fact]
    public void Read_ShouldRejectShortWindowSequence()
    {
        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.EightShortSequence, AacWindowShape.Sine, 10, 0, false);
        Assert.Throws<InvalidDataException>(() =>
            _reader.Read(new AacBitReader(Array.Empty<byte>()), info));
    }

    [Fact]
    public void Read_ShouldRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _reader.Read(null!, LongWindowInfo()));
        Assert.Throws<ArgumentNullException>(() => _reader.Read(new AacBitReader(Array.Empty<byte>()), null!));
    }

    private static AacIndividualChannelStreamInfo LongWindowInfo() =>
        new(AacWindowSequence.OnlyLongSequence, AacWindowShape.Sine, 20, 0, false);

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        public int Length { get; private set; }
        public void Write(int value, int count)
        {
            for (int bit = count - 1; bit >= 0; bit--)
            {
                if ((Length & 7) == 0) _bytes.Add(0);
                if (((value >> bit) & 1) != 0)
                    _bytes[^1] |= (byte)(1 << (7 - (Length & 7)));
                Length++;
            }
        }
        public byte[] ToArray() => _bytes.ToArray();
    }
}
