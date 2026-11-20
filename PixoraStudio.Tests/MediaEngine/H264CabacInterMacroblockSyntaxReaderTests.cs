using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacInterMacroblockSyntaxReaderTests
{
    [Fact]
    public void ReadPSlice_ShouldDecodeSingle16x16Partition()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0 });
        H264CabacMotionVectorNeighbourContext context = new();
        H264InterMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadPSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(0),
                0, 0, 0, 1, context);

        Assert.Single(syntax.Partitions);
        Assert.Equal((uint)0, syntax.Partitions[0].ReferenceIndex);
        Assert.Equal(new H264MotionVectorDifference(0, 0),
            syntax.Partitions[0].SubPartitions[0].MotionVectorDifferences[0]);
        Assert.Equal(new[] { 40, 47 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadPSlice_ShouldUpdateMvdNeighbourStateBetweenPartitions()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 1, 0, 0, 0, 0 },
            bypassBins: new[] { 0 });
        H264CabacMotionVectorNeighbourContext context = new();
        H264InterMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadPSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(1),
                0, 0, 0, 1, context);

        Assert.Equal(2, syntax.Partitions.Count);
        Assert.Equal(new H264MotionVectorDifference(1, 0),
            syntax.Partitions[0].SubPartitions[0].MotionVectorDifferences[0]);
        Assert.Equal(new H264MotionVectorDifference(0, 0),
            syntax.Partitions[1].SubPartitions[0].MotionVectorDifferences[0]);
        // The first partition's horizontal MVD is 1, below the context
        // threshold of 3; its vertical MVD is zero.
        Assert.Equal(new[] { 40, 43, 47, 40, 47 }, bins.ContextIndices);
    }

    
    [Fact]
    public void ReadPSlice_ShouldDeriveSecondPartitionMvdContextsIndependentlyByComponent()
    {
        // First partition: MVDx=0, MVDy=3. The second partition must
        // therefore use ctxIdx 40 for horizontal and ctxIdx 48 for vertical.
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0, 1, 1, 1, 0, 0, 0 },
            bypassBins: new[] { 0 });
        H264CabacMotionVectorNeighbourContext context = new();
        H264InterMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadPSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(2),
                0, 0, 0, 1, context);

        Assert.Equal(2, syntax.Partitions.Count);
        Assert.Equal(new H264MotionVectorDifference(0, 3),
            syntax.Partitions[0].SubPartitions[0].MotionVectorDifferences[0]);
        Assert.Equal(new H264MotionVectorDifference(0, 0),
            syntax.Partitions[1].SubPartitions[0].MotionVectorDifferences[0]);
        Assert.Equal(new[] { 40, 47, 50, 51, 52, 40, 48 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadPSlice_ShouldTraceMvdEscapePrefixSuffixAndSign()
    {
        int[] decisions = [1, 1, 1, 1, 1, 1, 1, 1, 1, 0];
        int[] bypass = [0, 0, 1, 1, 1];
        ScriptedCabacReader bins = new(decisions, bypass);
        H264CabacMotionVectorNeighbourContext context = new();

        H264InterMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadPSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(0),
                0, 0, 0, 1, context);

        Assert.Equal(new H264MotionVectorDifference(-12, 0),
            syntax.Partitions[0].SubPartitions[0].MotionVectorDifferences[0]);
        Assert.Contains("mvd syntax escape_prefix[0]=0", bins.Diagnostics);
        Assert.Contains("mvd syntax escape_suffix bit=2 value=0", bins.Diagnostics);
        Assert.Contains("mvd syntax escape_suffix bit=1 value=1", bins.Diagnostics);
        Assert.Contains("mvd syntax escape_suffix bit=0 value=1", bins.Diagnostics);
        Assert.Contains("mvd syntax sign=1", bins.Diagnostics);
        Assert.Contains("mvd syntax complete value=-12", bins.Diagnostics);
    }

    [Fact]
    public void ReadPSlice_ShouldDecodeFourEightByEightSubMacroblocks()
    {
        ScriptedCabacReader bins = new(new[] { 1, 1, 1, 1 }.Concat(Enumerable.Repeat(0, 8)).ToArray());
        H264CabacMotionVectorNeighbourContext context = new();
        H264InterMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadPSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(3),
                0, 0, 0, 1, context);

        Assert.True(syntax.UsesSubMacroblockPartitions);
        Assert.Equal(4, syntax.Partitions.Count);
        Assert.All(syntax.Partitions, partition =>
            Assert.Equal((8, 8), (partition.Width, partition.Height)));
        Assert.True(H264CabacInterMacroblockSyntaxReader
            .HasNoSubMacroblockPartitionSmallerThan8x8(syntax));
    }

    [Fact]
    public void ReadPSlice_ShouldForceReferenceZeroForP8x8Ref0()
    {
        ScriptedCabacReader bins = new(new[] { 1, 1, 1, 1 }.Concat(Enumerable.Repeat(0, 8)).ToArray());
        H264CabacMotionVectorNeighbourContext context = new();
        H264InterMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadPSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(4),
                2, 0, 0, 1, context);

        Assert.True(syntax.ReferenceIndexForcedToZero);
        Assert.All(syntax.Partitions, partition => Assert.Equal((uint)0, partition.ReferenceIndex));
        Assert.Equal(12, bins.ContextIndices.Count);
    }

    [Fact]
    public void ReadPSlice_ShouldTraceEveryMvdComponentAndItsDerivedContext()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0 });
        H264CabacMotionVectorNeighbourContext context = new();

        _ = new H264CabacInterMacroblockSyntaxReader()
            .ReadPSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(0),
                0, 0, 0, 1, context);

        Assert.Contains(
            "mvd list=0 block=(0,0) region=(0,0,16,16) component=x context=40 increment=0 begin",
            bins.Diagnostics);
        Assert.Contains(
            "mvd list=0 block=(0,0) region=(0,0,16,16) component=x value=0",
            bins.Diagnostics);
        Assert.Contains(
            "mvd list=0 block=(0,0) region=(0,0,16,16) component=y context=47 increment=0 begin",
            bins.Diagnostics);
        Assert.Contains(
            "mvd list=0 block=(0,0) region=(0,0,16,16) component=y value=0",
            bins.Diagnostics);
        Assert.Contains(
            "mvd syntax base=40 increment=0 first_context=40 begin",
            bins.Diagnostics);
        Assert.Contains(
            "mvd syntax first_bin context=40 value=0",
            bins.Diagnostics);
        Assert.Contains(
            "mvd syntax complete value=0",
            bins.Diagnostics);
        Assert.Contains(
            "mvd list=0 block=(0,0) region=(0,0,16,16) complete value=(0,0)",
            bins.Diagnostics);
    }

    [Fact]
    public void ReadBSlice_ShouldDecodeDirectMacroblockWithoutBins()
    {
        ScriptedCabacReader bins = new();
        H264CabacMotionVectorNeighbourContext context = new();
        H264BMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadBSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(0),
                0, 0, 0, 0, 1, context);

        Assert.True(H264CabacInterMacroblockSyntaxReader.IsDirectMacroblock(syntax));
        Assert.Equal(H264BPredictionDirection.Direct, syntax.Partitions[0].Direction);
        Assert.Empty(bins.ContextIndices);
    }

    [Fact]
    public void ReadBSlice_ShouldDecodeList0Forward16x16Partition()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0 });
        H264CabacMotionVectorNeighbourContext context = new();

        H264BMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadBSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(1),
                0, 0, 0, 0, 1, context);

        H264BPredictionPartition partition = Assert.Single(syntax.Partitions);
        Assert.Equal(H264BPredictionDirection.List0, partition.Direction);
        Assert.NotNull(partition.SubPartitions[0].MotionVectorDifferenceL0);
        Assert.Null(partition.SubPartitions[0].MotionVectorDifferenceL1);
        Assert.Equal(new[] { 40, 47 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBSlice_ShouldDecodeList1Backward16x16Partition()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0 });
        H264CabacMotionVectorNeighbourContext context = new();

        H264BMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadBSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(2),
                0, 0, 0, 0, 1, context);

        H264BPredictionPartition partition = Assert.Single(syntax.Partitions);
        Assert.Equal(H264BPredictionDirection.List1, partition.Direction);
        Assert.Null(partition.SubPartitions[0].MotionVectorDifferenceL0);
        Assert.NotNull(partition.SubPartitions[0].MotionVectorDifferenceL1);
        Assert.Equal(new[] { 40, 47 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBSlice_ShouldDecodeBiDirectional16x16Partition()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0, 0, 0 });
        H264CabacMotionVectorNeighbourContext context = new();
        H264BMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadBSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(3),
                0, 0, 0, 0, 1, context);

        Assert.Single(syntax.Partitions);
        Assert.Equal(H264BPredictionDirection.Bi, syntax.Partitions[0].Direction);
        Assert.NotNull(syntax.Partitions[0].SubPartitions[0].MotionVectorDifferenceL0);
        Assert.NotNull(syntax.Partitions[0].SubPartitions[0].MotionVectorDifferenceL1);
        Assert.Equal(new[] { 40, 47, 40, 47 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBSlice_ShouldDecodeFourDirectEightByEightSubMacroblocks()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0, 0, 0 });
        H264CabacMotionVectorNeighbourContext context = new();
        H264BMacroblockSyntax syntax = new H264CabacInterMacroblockSyntaxReader()
            .ReadBSlice(new H264CabacSyntaxElementReader(bins), CreateHeader(22),
                0, 0, 0, 0, 1, context);

        Assert.True(syntax.UsesSubMacroblockPartitions);
        Assert.Equal(4, syntax.Partitions.Count);
        Assert.True(H264CabacInterMacroblockSyntaxReader.IsDirectMacroblock(syntax));
        Assert.All(syntax.Partitions, partition =>
            Assert.Equal(H264BPredictionDirection.Direct, partition.Direction));
    }

    private static H264MacroblockHeader CreateHeader(uint macroblockType) =>
        new(0, macroblockType, false, 0, false, false,
            null, null, null, false);

    private sealed class ScriptedCabacReader : IH264CabacBinReader, IH264CabacDiagnosticTraceSink
    {
        private readonly Queue<int> _decisionBins;
        private readonly Queue<int> _bypassBins;

        public ScriptedCabacReader(params int[] bins)
            : this(bins, Array.Empty<int>())
        {
        }

        public ScriptedCabacReader(
            IEnumerable<int> decisionBins,
            IEnumerable<int>? bypassBins = null)
        {
            _decisionBins = new Queue<int>(decisionBins);
            _bypassBins = new Queue<int>(bypassBins ?? []);
        }

        public List<int> ContextIndices { get; } = [];
        public List<string> Diagnostics { get; } = [];
        public void TraceDiagnostic(string marker) => Diagnostics.Add(marker);

        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _decisionBins.Dequeue();
        }
        public int DecodeBypass() => _bypassBins.Dequeue();
        public int DecodeTerminate() => throw new InvalidOperationException();
        public void MarkTerminated() => throw new InvalidOperationException();
    }
}
