using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DecodedPictureSurfaceTests
{
    [Theory]
    [InlineData(1, 32, 16, 16, 8)]
    [InlineData(2, 32, 16, 16, 16)]
    public void Constructor_ShouldCreateCorrectPlaneGeometry(
        uint format, int width, int height, int chromaWidth, int chromaHeight)
    {
        var surface = new H264DecodedPictureSurface(CreateSps(format, 1, 0));
        Assert.Equal(width, surface.Width);
        Assert.Equal(height, surface.Height);
        Assert.Equal(chromaWidth, surface.ChromaWidth);
        Assert.Equal(chromaHeight, surface.ChromaHeight);
    }

    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 128)]
    public void WriteIntraMacroblock_ShouldCopyAllPlanesAndPublishAvailability(uint format, int chromaCount)
    {
        var surface = new H264DecodedPictureSurface(CreateSps(format, 1, 0));
        byte[] y = Enumerable.Repeat((byte)11, 256).ToArray();
        byte[] cb = Enumerable.Repeat((byte)22, chromaCount).ToArray();
        byte[] cr = Enumerable.Repeat((byte)33, chromaCount).ToArray();

        surface.WriteIntraMacroblock(0, 0, 7, y, cb, cr);

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.True(surface.IsMacroblockAvailable(0, 0, 7, false));
        Assert.False(surface.IsMacroblockAvailable(0, 0, 8, false));
        Assert.Equal((byte)11, surface.GetLumaSample(15, 15));
        Assert.Equal((byte)22, surface.GetChromaSample(1, 7, format == 1 ? 7 : 15));
        Assert.Equal((byte)33, surface.GetChromaSample(2, 7, format == 1 ? 7 : 15));
    }

    [Fact]
    public void WriteMacroblock_ShouldCopyArrayBackedPlanesIntoCorrectFrameRegion()
    {
        H264DecodedPictureSurface surface = new(CreateSps(1, 1, 0));
        byte[] luma = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        byte[] cb = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();
        byte[] cr = Enumerable.Range(101, 64).Select(value => (byte)value).ToArray();

        surface.WriteMacroblock(1, 0, 0, false, luma, cb, cr);

        Assert.Equal((byte)0, surface.GetLumaSample(16, 0));
        Assert.Equal((byte)255, surface.GetLumaSample(31, 15));
        Assert.Equal((byte)1, surface.GetChromaSample(1, 8, 0));
        Assert.Equal((byte)64, surface.GetChromaSample(1, 15, 7));
        Assert.Equal((byte)101, surface.GetChromaSample(2, 8, 0));
        Assert.Equal((byte)164, surface.GetChromaSample(2, 15, 7));

        luma[0] = 99;
        cb[0] = 99;
        cr[0] = 99;
        Assert.Equal((byte)0, surface.GetLumaSample(16, 0));
        Assert.Equal((byte)1, surface.GetChromaSample(1, 8, 0));
        Assert.Equal((byte)101, surface.GetChromaSample(2, 8, 0));
    }

    [Fact]
    public void WriteMacroblock_ShouldDistinguishInterFromIntraForConstrainedPrediction()
    {
        var surface = new H264DecodedPictureSurface(CreateSps(1, 1, 0));
        surface.WriteMacroblock(0, 0, 2, false,
            new byte[256], new byte[64], new byte[64]);

        Assert.True(surface.IsMacroblockAvailable(0, 0, 2, false));
        Assert.False(surface.IsMacroblockAvailable(0, 0, 2, true));
    }

    [Fact]
    public void WriteIntraMacroblock_ShouldRejectWrongPlaneSizesWithoutPublishing()
    {
        var surface = new H264DecodedPictureSurface(CreateSps(1, 1, 0));
        Assert.Throws<ArgumentException>(() => surface.WriteIntraMacroblock(
            0, 0, 0, new byte[256], new byte[63], new byte[64]));
        Assert.False(surface.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void Constructor_ShouldRejectUnsupportedPictureFormats()
    {
        Assert.Throws<NotSupportedException>(() => new H264DecodedPictureSurface(
            CreateSps(3, 0, 0)));
        Assert.Throws<NotSupportedException>(() => new H264DecodedPictureSurface(
            CreateSps(1, 0, 0) with { BitDepthLumaMinus8 = 1 }));
        Assert.Throws<NotSupportedException>(() => new H264DecodedPictureSurface(
            CreateSps(1, 1, 0) with { FrameMbsOnlyFlag = false }));
    }

    [Fact]
    public void GetUndecodedMacroblockAddresses_ShouldReturnMissingAddressesInRasterScanOrder()
    {
        H264DecodedPictureSurface surface = new(CreateSps(1, 2, 1));
        surface.WriteMacroblock(0, 0, 0, false,
            new byte[256], new byte[64], new byte[64]);
        surface.WriteMacroblock(2, 0, 0, false,
            new byte[256], new byte[64], new byte[64]);
        surface.WriteMacroblock(1, 1, 0, false,
            new byte[256], new byte[64], new byte[64]);

        Assert.Equal(new uint[] { 1, 3, 5 }, surface.GetUndecodedMacroblockAddresses());
    }

    [Fact]
    public async Task WriteMacroblock_ShouldRejectWriterWhileSameMacroblockIsBeingReconstructed()
    {
        H264DecodedPictureSurface surface = new(CreateSps(1, 0, 0));
        using BlockingReadOnlyByteList blockingLuma =
            new(Enumerable.Repeat((byte)10, 256).ToArray());

        Task firstWriter = Task.Run(() => surface.WriteMacroblock(
            0, 0, 3, false, blockingLuma, new byte[64], new byte[64]));

        Assert.True(blockingLuma.WaitUntilRead(TimeSpan.FromSeconds(5)));

        Assert.Throws<InvalidOperationException>(() => surface.WriteMacroblock(
            0, 0, 4, false, new byte[256], new byte[64], new byte[64]));

        blockingLuma.Release();
        await firstWriter;

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.True(surface.IsMacroblockAvailable(0, 0, 3, false));
        Assert.False(surface.IsMacroblockAvailable(0, 0, 4, false));
        Assert.True(surface.IsComplete);
    }

    [Fact]
    public void WriteMacroblock_ShouldRejectSequentialRewriteFromAnotherSlice()
    {
        H264DecodedPictureSurface surface = new(CreateSps(1, 0, 0));
        surface.WriteMacroblock(0, 0, 1, false,
            Enumerable.Repeat((byte)10, 256).ToArray(),
            new byte[64], new byte[64]);

        Assert.Throws<InvalidOperationException>(() => surface.WriteMacroblock(
            0, 0, 2, false,
            Enumerable.Repeat((byte)20, 256).ToArray(),
            new byte[64], new byte[64]));

        Assert.True(surface.IsMacroblockAvailable(0, 0, 1, false));
        Assert.Equal((byte)10, surface.GetLumaSample(0, 0));
    }

    [Fact]
    public void WriteMacroblock_ShouldSupportConcurrentWritesToDistinctMacroblocks()
    {
        H264DecodedPictureSurface surface = new(CreateSps(1, 1, 0));

        Parallel.For(0, 2, x =>
        {
            byte value = checked((byte)(x + 10));
            surface.WriteMacroblock(x, 0, x, false,
                Enumerable.Repeat(value, 256).ToArray(),
                Enumerable.Repeat(value, 64).ToArray(),
                Enumerable.Repeat(value, 64).ToArray());
        });

        Assert.True(surface.IsComplete);
        Assert.Equal((byte)10, surface.GetLumaSample(0, 0));
        Assert.Equal((byte)11, surface.GetLumaSample(16, 0));
        Assert.True(surface.IsMacroblockAvailable(0, 0, 0, false));
        Assert.True(surface.IsMacroblockAvailable(1, 0, 1, false));
    }

    [Fact]
    public void WriteMacroblock_ShouldRepublishUpdatedDataAfterSequentialRewrite()
    {
        H264DecodedPictureSurface surface = new(CreateSps(1, 0, 0));

        surface.WriteMacroblock(0, 0, 2, false,
            Enumerable.Repeat((byte)10, 256).ToArray(),
            Enumerable.Repeat((byte)11, 64).ToArray(),
            Enumerable.Repeat((byte)12, 64).ToArray());
        surface.SetDeblockingMetadata(0, 0, CreateDeblockingMetadata());

        surface.WriteMacroblock(0, 0, 2, true,
            Enumerable.Repeat((byte)20, 256).ToArray(),
            Enumerable.Repeat((byte)21, 64).ToArray(),
            Enumerable.Repeat((byte)22, 64).ToArray());

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.True(surface.IsMacroblockAvailable(0, 0, 2, true));
        Assert.False(surface.IsMacroblockAvailable(0, 0, 1, false));
        Assert.Equal((byte)20, surface.GetLumaSample(0, 0));
        Assert.Equal((byte)21, surface.GetChromaSample(1, 0, 0));
        Assert.Equal((byte)22, surface.GetChromaSample(2, 0, 0));
    }

    private static H264DeblockingMacroblockMetadata CreateDeblockingMetadata()
    {
        H264DeblockingBlockMetadata block =
            new(false, false, null, null, null, null);
        return new H264DeblockingMacroblockMetadata(
            27, 25, 26, 0, 0, 0, false,
            Enumerable.Repeat(block, 16).ToArray(),
            Enumerable.Repeat(block, 4).ToArray(),
            Enumerable.Repeat(block, 4).ToArray());
    }

    private sealed class BlockingReadOnlyByteList(byte[] values) :
        IReadOnlyList<byte>, IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _release = new(false);

        public int Count => values.Length;

        public byte this[int index]
        {
            get
            {
                _entered.Set();
                _release.Wait();
                return values[index];
            }
        }

        public bool WaitUntilRead(TimeSpan timeout) => _entered.Wait(timeout);

        public void Release() => _release.Set();

        public IEnumerator<byte> GetEnumerator() =>
            ((IEnumerable<byte>)values).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            values.GetEnumerator();

        public void Dispose()
        {
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private static H264SequenceParameterSet CreateSps(
        uint format, uint widthMinus1, uint heightMinus1) =>
        new(0, 66, 30, format, false, 0, 0, 0, 0, 0, 1,
            widthMinus1, heightMinus1, true, true,
            (widthMinus1 + 1) * 16, (heightMinus1 + 1) * 16, false);
}
