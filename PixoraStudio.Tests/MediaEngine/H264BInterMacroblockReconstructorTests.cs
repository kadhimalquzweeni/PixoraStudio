using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BInterMacroblockReconstructorTests
{
    [Fact]
    public void DecodeAndReconstruct_L0Macroblock_ShouldCommitPredictionAndZeroResidual()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps = CreatePps();
        H264DecodedPictureSurface reference = CreateSurface(sps, 73);
        H264DecodedPictureSurface destination = new(sps);
        H264BReferencePictureLists references = new(
            [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 2)]);
        BitWriter bits = new();
        bits.Se(0); bits.Se(0); // MVD x/y
        bits.Ue(0); // inter coded_block_pattern = 0
        H264MacroblockHeader header = Header(1);

        H264BInterMacroblockReconstructionResult result =
            new H264BInterMacroblockReconstructor().DecodeAndReconstruct(
                bits.Reader(), header, 0, 0,
                [reference], [reference], references,
                new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1),
                destination, 0, 0, 0, 26, 1, true,
                sps, pps,
                new H264CavlcNeighbourContext(),
                new H264CavlcNeighbourContext(),
                new H264CavlcNeighbourContext());

        Assert.False(result.IsSkipped);
        Assert.Equal(26, result.QpY);
        Assert.Equal((byte)73, destination.GetLumaSample(0, 0));
        Assert.True(destination.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void DecodeSkippedAndReconstruct_SpatialDirect_ShouldCommitPredictionWithoutResidual()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps = CreatePps();
        H264DecodedPictureSurface reference = CreateSurface(sps, 91);
        H264DecodedPictureSurface destination = new(sps);
        H264BReferencePictureLists references = new(
            [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 4)]);
        H264MacroblockHeader skipped = new(
            0, 0, true, 1, false, false, null, null, null, false);

        H264BInterMacroblockReconstructionResult result =
            new H264BInterMacroblockReconstructor().DecodeSkippedAndReconstruct(
                skipped, [reference], [reference], references,
                new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1),
                destination, 0, 0, 0, 26, 2, true, sps, pps);

        Assert.True(result.IsSkipped);
        Assert.Equal(26, result.QpY);
        Assert.Equal((byte)91, destination.GetLumaSample(0, 0));
        Assert.True(destination.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void DecodeSkippedAndReconstruct_ShouldRejectTemporalDirectUntilCollocatedMetadataExists()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps = CreatePps();
        H264DecodedPictureSurface reference = CreateSurface(sps, 10);
        H264MacroblockHeader skipped = new(
            0, 0, true, 1, false, false, null, null, null, false);

        Assert.Throws<NotSupportedException>(() =>
            new H264BInterMacroblockReconstructor().DecodeSkippedAndReconstruct(
                skipped, [reference], [reference],
                new H264BReferencePictureLists(
                    [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 1)]),
                new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1),
                new H264DecodedPictureSurface(sps), 0, 0, 0,
                26, 0, false, sps, pps));
    }

    private static H264MacroblockHeader Header(uint type) =>
        new(type, type, false, 0, false, false, null, null, null, false);

    private static H264SequenceParameterSet CreateSps() => new(
        0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
        0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);

    private static H264DecodedPictureSurface CreateSurface(
        H264SequenceParameterSet sps, byte value)
    {
        H264DecodedPictureSurface surface = new(sps);
        surface.WriteMacroblock(
            0, 0, 0, false,
            Enumerable.Repeat(value, 256).ToArray(),
            Enumerable.Repeat(value, 64).ToArray(),
            Enumerable.Repeat(value, 64).ToArray());
        return surface;
    }

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];
        public void Ue(uint value)
        {
            uint code = value + 1; int zeros = 0;
            for (uint n = code; n > 1; n >>= 1) zeros++;
            for (int i = 0; i < zeros; i++) _bits.Add(false);
            for (int i = zeros; i >= 0; i--) _bits.Add(((code >> i) & 1) != 0);
        }
        public void Se(int value) => Ue(value <= 0
            ? checked((uint)(-2L * value))
            : checked((uint)(2L * value - 1)));
        public H264BitReader Reader()
        {
            byte[] data = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i]) data[i / 8] |= (byte)(1 << (7 - i % 8));
            return new H264BitReader(data, _bits.Count);
        }
    }
}
