using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacIndividualChannelStreamDataReaderTests
{
    private readonly AacIndividualChannelStreamDataReader _reader = new();

    [Fact]
    public void Read_ShouldParseEmptyLongWindowIcsInSyntaxOrder()
    {
        var bits = new BitWriter();
        bits.Write(100, 8); // global_gain
        bits.Write(0, 1);   // reserved
        bits.Write(0, 2);   // ONLY_LONG_SEQUENCE
        bits.Write(0, 1);   // window_shape
        bits.Write(0, 6);   // max_sfb
        bits.Write(0, 1);   // predictor_data_present
        bits.Write(0, 1);   // pulse_data_present
        bits.Write(0, 1);   // tns_data_present
        bits.Write(0, 1);   // gain_control_data_present

        var reader = new AacBitReader(bits.ToArray(), bits.Length);
        var result = _reader.Read(reader, samplingFrequencyIndex: 4);

        Assert.Equal(100, result.GlobalGain);
        Assert.Equal(AacWindowSequence.OnlyLongSequence, result.StreamInfo.WindowSequence);
        Assert.Empty(result.Sections);
        Assert.Empty(result.ScaleFactors.Bands);
        Assert.Null(result.PulseData);
        Assert.Empty(result.TnsData.Windows);
        Assert.Empty(result.SpectralBands);
        Assert.Equal(bits.Length, reader.PositionBits);
    }

    [Fact]
    public void Read_ShouldUseSharedIcsInfoWhenCommonWindowIsSet()
    {
        var bits = new BitWriter();
        bits.Write(90, 8); // global_gain
        bits.Write(0, 1);  // pulse_data_present
        bits.Write(0, 1);  // tns_data_present
        bits.Write(0, 1);  // gain_control_data_present

        var sharedInfo = new AacIndividualChannelStreamInfo(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine,
            0,
            0,
            false);

        var reader = new AacBitReader(bits.ToArray(), bits.Length);
        var result = _reader.Read(
            reader, samplingFrequencyIndex: 4, commonWindow: true,
            sharedStreamInfo: sharedInfo);

        Assert.Same(sharedInfo, result.StreamInfo);
        Assert.Equal(bits.Length, reader.PositionBits);
    }

    [Fact]
    public void Read_ShouldParsePulseAndTnsFlagsAndPayloads()
    {
        var bits = new BitWriter();
        bits.Write(100, 8); // global_gain
        bits.Write(1, 1);   // pulse_data_present
        bits.Write(0, 2);   // one pulse
        bits.Write(3, 6);   // pulse_start_sfb
        bits.Write(2, 5);   // pulse offset
        bits.Write(7, 4);   // pulse amplitude
        bits.Write(1, 1);   // tns_data_present
        bits.Write(0, 2);   // long-window n_filt = 0
        bits.Write(0, 1);   // gain_control_data_present

        var sharedInfo = new AacIndividualChannelStreamInfo(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine,
            0,
            0,
            false);

        var result = _reader.Read(
            new AacBitReader(bits.ToArray(), bits.Length),
            samplingFrequencyIndex: 4,
            commonWindow: true,
            sharedStreamInfo: sharedInfo);

        var pulse = Assert.Single(Assert.IsType<AacPulseData>(result.PulseData).Pulses);
        Assert.Equal(3, result.PulseData!.PulseStartScaleFactorBand);
        Assert.Equal(new AacPulse(2, 7), pulse);
        Assert.Single(result.TnsData.Windows);
        Assert.Empty(result.TnsData.Windows[0].Filters);
    }

    [Fact]
    public void Read_ShouldRejectMissingSharedInfoForCommonWindow()
    {
        var bits = new BitWriter();
        bits.Write(80, 8);

        Assert.Throws<ArgumentNullException>(() =>
            _reader.Read(new AacBitReader(bits.ToArray(), bits.Length), 4, commonWindow: true));
    }

    [Fact]
    public void Read_ShouldRejectGainControlDataForAacLc()
    {
        var bits = new BitWriter();
        bits.Write(80, 8);
        bits.Write(0, 1); // pulse
        bits.Write(0, 1); // tns
        bits.Write(1, 1); // gain control present

        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine, 0, 0, false);

        Assert.Throws<NotSupportedException>(() =>
            _reader.Read(new AacBitReader(bits.ToArray(), bits.Length), 4, true, info));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    public void Read_ShouldRejectInvalidSamplingFrequencyIndex(int index)
    {
        var bits = new BitWriter();
        bits.Write(80, 8);
        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine, 0, 0, false);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _reader.Read(new AacBitReader(bits.ToArray(), bits.Length), index, true, info));
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        public int Length { get; private set; }

        public void Write(int value, int count)
        {
            for (int bit = count - 1; bit >= 0; bit--)
            {
                if ((Length & 7) == 0)
                    _bytes.Add(0);
                if (((value >> bit) & 1) != 0)
                    _bytes[^1] |= (byte)(1 << (7 - (Length & 7)));
                Length++;
            }
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}