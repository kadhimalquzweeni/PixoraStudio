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
        H264DeblockingMacroblockMetadata metadata =
            destination.GetDeblockingMetadata(0, 0)!;
        Assert.Equal(26, metadata.QpY);
        Assert.Equal(16, metadata.LumaBlocks.Count);
        Assert.Equal(new H264ReferencePicture(0, 0), metadata.LumaBlocks[0].ReferencePictureL0);
        Assert.Null(metadata.LumaBlocks[0].ReferencePictureL1);
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
                destination, 0, 0, 0, 26, 2, true, sps, pps,
                collocatedContextProvider: (_, _, _, _) => null);

        Assert.True(result.IsSkipped);
        Assert.Equal(26, result.QpY);
        Assert.Equal((byte)91, destination.GetLumaSample(0, 0));
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        H264DeblockingMacroblockMetadata metadata =
            destination.GetDeblockingMetadata(0, 0)!;
        Assert.Equal(26, metadata.QpY);
        Assert.Equal(16, metadata.LumaBlocks.Count);
        Assert.True(metadata.LumaBlocks.All(block => block.IsIntra == false));
    }

    [Fact]
    public void DecodeSkippedAndReconstruct_TemporalDirect_ShouldUseProvidedCollocatedMotion()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps = CreatePps();
        H264DecodedPictureSurface reference = CreateSurface(sps, 91);
        H264DecodedPictureSurface destination = new(sps);
        H264MacroblockHeader skipped = new(
            0, 0, true, 1, false, false, null, null, null, false);

        H264BInterMacroblockReconstructionResult result =
            new H264BInterMacroblockReconstructor().DecodeSkippedAndReconstruct(
                skipped, [reference], [reference],
                new H264BReferencePictureLists(
                    [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 4)]),
                new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1),
                destination, 0, 0, 0, 26, 2, false, sps, pps,
                collocatedContextProvider: (_, _, _, _) =>
                    new H264TemporalDirectPredictionContext(
                        new H264MotionVector(0, 0), 2, 0, 0, false));

        Assert.True(result.IsSkipped);
        Assert.Equal(4, result.Partitions.Count);
        Assert.Equal((byte)91, destination.GetLumaSample(0, 0));
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


    [Fact]
    public void ReconstructCabac_L0Macroblock_ShouldCommitPredictionAndResidual()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps = CreatePps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = CreateSurface(sps, 73);
        H264DecodedPictureSurface destination = new(sps);
        H264BReferencePictureLists references = new(
            [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 2)]);
        H264MacroblockHeader header = Header(1);
        H264BMacroblockSyntax syntax = new(
            1, false,
            [new H264BPredictionPartition(0, 0, 16, 16,
                H264BPredictionDirection.List0, 0, null,
                [new H264BSubPartition(0, 0, 16, 16,
                    H264BPredictionDirection.List0,
                    new H264MotionVectorDifference(0, 0), null)])]);
        H264InterMacroblockResidualSyntax residualSyntax = new(
            new H264CodedBlockPattern(0, 0, 0, 1), null);
        H264InterResidualMacroblock residual = EmptyResidual(0);

        H264BInterMacroblockReconstructionResult result =
            new H264BInterMacroblockReconstructor().ReconstructCabac(
                header, syntax, residualSyntax, residual,
                [reference], [reference], references,
                new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1),
                destination, 0, 0, 0, 26, 1, true, sps, pps);

        Assert.False(result.IsSkipped);
        Assert.Equal(26, result.QpY);
        Assert.Equal((byte)73, destination.GetLumaSample(0, 0));
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.Equal(16, destination.GetDeblockingMetadata(0, 0)!.LumaBlocks.Count);
    }

    [Fact]
    public void DecodeSkippedAndReconstruct_ShouldAllowCabacPps()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps = CreatePps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = CreateSurface(sps, 91);
        H264DecodedPictureSurface destination = new(sps);
        H264MacroblockHeader skipped = new(
            0, 0, true, 1, false, false, null, null, null, false);

        H264BInterMacroblockReconstructionResult result =
            new H264BInterMacroblockReconstructor().DecodeSkippedAndReconstruct(
                skipped, [reference], [reference],
                new H264BReferencePictureLists(
                    [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 4)]),
                new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1),
                destination, 0, 0, 0, 26, 2, true, sps, pps,
                collocatedContextProvider: (_, _, _, _) => null);

        Assert.True(result.IsSkipped);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
    }

    private static H264InterResidualMacroblock EmptyResidual(uint address)
    {
        H264IntraLumaResidualBlock[] luma = Enumerable.Range(0, 16)
            .Select(index => new H264IntraLumaResidualBlock(
                index % 4, index / 4, false,
                new H264CavlcResidualBlock(16, 0, 0, 0, new int[16])))
            .ToArray();
        H264IntraChromaResidualComponent[] chroma = new[] { 1, 2 }
            .Select(component => new H264IntraChromaResidualComponent(
                (byte)component, null,
                Enumerable.Range(0, 4)
                    .Select(index => new H264IntraChromaResidualBlock(
                        index % 2, index / 2, false,
                        new H264CavlcResidualBlock(16, 0, 0, 0, new int[16])))
                    .ToArray()))
            .ToArray();
        return new H264InterResidualMacroblock(address, luma, chroma);
    }


    [Fact]
    public void DecodeAndReconstruct_ShouldRejectCabacBeforeCavlcSyntaxParsing()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264PictureParameterSetHeader pps = CreatePps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = CreateSurface(sps, 73);
        H264MacroblockHeader header = Header(1);

        Assert.Throws<NotSupportedException>(() =>
            new H264BInterMacroblockReconstructor().DecodeAndReconstruct(
                new BitWriter().Reader(), header, 0, 0,
                [reference], [reference],
                new H264BReferencePictureLists(
                    [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 2)]),
                new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1),
                new H264DecodedPictureSurface(sps), 0, 0, 0, 26, 1, true,
                sps, pps,
                new H264CavlcNeighbourContext(),
                new H264CavlcNeighbourContext(),
                new H264CavlcNeighbourContext()));
    }


    private static H264MacroblockHeader Header(uint type) =>
        new(0, type, false, 0, false, false, null, null, null, false);

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
