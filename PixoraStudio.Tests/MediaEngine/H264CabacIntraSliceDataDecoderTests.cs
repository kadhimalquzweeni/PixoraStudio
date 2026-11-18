using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacIntraSliceDataDecoderTests
{
    [Fact]
    public void Decode_CabacIntra4x4Slice_ShouldTraverseAndReconstructMacroblock()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps =
            CreatePps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface surface = new(sps);
        BitWriter bits = WriteIdrISliceHeader();
        bits.AlignWithOneBits();
        bits.WriteBits(0, 9);
        bits.WriteRbspTrailingBits();

        int[] decisionBins =
        [
            0,
            .. Enumerable.Repeat(1, 16),
            0,
            0, 0, 0, 0,
            0
        ];
        ScriptedCabacSliceDataReader cabac = new(decisionBins);
        H264CabacIntraSliceDataDecoder decoder = new(
            surface,
            (reader, qpY, isIntra, cabacInitIdc) =>
            {
                Assert.Equal(26, qpY);
                Assert.True(isIntra);
                Assert.Equal(0, cabacInitIdc);
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264IntraSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x65, bits.ToArray()), sps, pps, 6);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Equal(26, result.FinalQpY);
        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.True(surface.HasCompleteDeblockingMetadata);
        Assert.All(surface.CopyLumaPlane(), value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCbPlane(), value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCrPlane(), value => Assert.Equal((byte)128, value));
        Assert.Equal(
            new[] { 3 }
                .Concat(Enumerable.Repeat(68, 16))
                .Concat(new[] { 64, 73, 74, 75, 76, 77 }),
            cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
        Assert.True(result.BitsConsumed > 0);
    }


    [Fact]
    public void Decode_CabacIpcmSlice_ShouldReadRawSamplesAndRestartArithmeticEngine()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps =
            CreatePps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface surface = new(sps);
        BitWriter bits = WriteIdrISliceHeader();
        bits.AlignWithOneBits();
        bits.WriteBits(0, 9);
        bits.AlignWithZeroBits();
        bits.WriteRepeatedByte(0x5A, 256);
        bits.WriteRepeatedByte(0x6B, 64);
        bits.WriteRepeatedByte(0x7C, 64);
        bits.WriteRbspTrailingBits();

        ScriptedCabacSliceDataReader cabac = new(
            decisionBins: [1],
            terminateBins: [1, 1]);
        H264CabacIntraSliceDataDecoder decoder = new(
            surface,
            (reader, _, _, _) =>
            {
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264IntraSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x65, bits.ToArray()), sps, pps, 2);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.Equal(1, cabac.RestartAfterPcmCount);
        Assert.True(cabac.IsTerminated);
        Assert.All(surface.CopyLumaPlane(), value => Assert.Equal((byte)0x5A, value));
        Assert.All(surface.CopyCbPlane(), value => Assert.Equal((byte)0x6B, value));
        Assert.All(surface.CopyCrPlane(), value => Assert.Equal((byte)0x7C, value));
    }

    private static H264SequenceParameterSet CreateSps() =>
        new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);

    private static BitWriter WriteIdrISliceHeader()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(2); // I slice
        bits.WriteUe(0); // PPS id
        bits.WriteBits(0, 4); // frame_num
        bits.WriteUe(0); // idr_pic_id
        bits.WriteBits(0, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // no_output_of_prior_pics_flag
        bits.WriteFlag(false); // long_term_reference_flag
        bits.WriteSe(0); // slice_qp_delta
        return bits;
    }

    private sealed class ScriptedCabacSliceDataReader : IH264CabacSliceDataReader
    {
        private readonly Queue<int> _decisionBins;
        private readonly Queue<int> _terminateBins;

        public ScriptedCabacSliceDataReader(
            IEnumerable<int> decisionBins,
            IEnumerable<int>? terminateBins = null)
        {
            _decisionBins = new Queue<int>(decisionBins);
            _terminateBins = new Queue<int>(terminateBins ?? [1]);
        }

        public List<int> ContextIndices { get; } = [];
        public bool IsTerminated { get; private set; }
        public int RestartAfterPcmCount { get; private set; }

        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _decisionBins.Dequeue();
        }

        public int DecodeBypass() =>
            throw new InvalidOperationException("This I_NxN test does not use bypass bins.");

        public int DecodeTerminate() => _terminateBins.Dequeue();
        public void MarkTerminated() => IsTerminated = true;
        public void RestartAfterPcm() => RestartAfterPcmCount++;
    }

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public void WriteFlag(bool value) => _bits.Add(value);

        public void WriteBits(uint value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                _bits.Add(((value >> i) & 1) != 0);
        }

        public void WriteUe(uint value)
        {
            uint codeNum = checked(value + 1);
            int leadingZeroes = 0;
            for (uint current = codeNum; current > 1; current >>= 1)
                leadingZeroes++;
            for (int i = 0; i < leadingZeroes; i++)
                _bits.Add(false);
            WriteBits(codeNum, leadingZeroes + 1);
        }

        public void WriteSe(int value)
        {
            uint codeNum = value <= 0
                ? checked((uint)(-2L * value))
                : checked((uint)(2L * value - 1));
            WriteUe(codeNum);
        }

        public void AlignWithOneBits()
        {
            while ((_bits.Count & 7) != 0)
                _bits.Add(true);
        }

        public void AlignWithZeroBits()
        {
            while ((_bits.Count & 7) != 0)
                _bits.Add(false);
        }

        public void WriteRepeatedByte(byte value, int count)
        {
            for (int i = 0; i < count; i++)
                WriteBits(value, 8);
        }

        public void WriteRbspTrailingBits()
        {
            WriteFlag(true);
            while ((_bits.Count & 7) != 0)
                WriteFlag(false);
        }

        public byte[] ToArray()
        {
            byte[] bytes = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i])
                    bytes[i / 8] |= (byte)(1 << (7 - i % 8));
            return bytes;
        }
    }
}
