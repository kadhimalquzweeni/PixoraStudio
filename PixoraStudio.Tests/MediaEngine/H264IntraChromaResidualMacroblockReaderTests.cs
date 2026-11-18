using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraChromaResidualMacroblockReaderTests
{
    private readonly H264IntraLumaResidualMacroblockReader _reader = new();

    [Theory]
    [InlineData(1, 4, 2)]
    [InlineData(2, 8, 4)]
    public void Read_ShouldDecodeChromaDcAndAllAcBlocksForCodedPatternTwo(
        uint chromaFormat, int expectedDcCoefficients, int expectedBlocksHigh)
    {
        int blockCount = 2 * expectedBlocksHigh;
        string dcZeroToken = chromaFormat == 1 ? "01" : "1";
        // Syntax order: Cb DC, Cr DC, all Cb AC blocks, then all Cr AC blocks.
        string bitstream = dcZeroToken + dcZeroToken + new string('1', blockCount * 2);
        H264BitReader bits = CreateReader(bitstream);
        H264CavlcNeighbourContext cb = new();
        H264CavlcNeighbourContext cr = new();

        H264IntraLumaResidualMacroblock result = _reader.Read(
            bits, CreateHeader(), CreateSyntax(chromaFormat, 2),
            CreateSps(chromaFormat), 0, 0, 4,
            new H264CavlcNeighbourContext(), cb, cr);

        Assert.Equal(2, result.ChromaComponents.Count);
        Assert.Equal(new byte[] { 1, 2 },
            result.ChromaComponents.Select(component => component.ComponentId));
        foreach (H264IntraChromaResidualComponent component in result.ChromaComponents)
        {
            Assert.NotNull(component.Dc);
            Assert.Equal(expectedDcCoefficients, component.Dc.MaximumCoefficients);
            Assert.Equal(blockCount, component.Blocks.Count);
            Assert.All(component.Blocks, block =>
            {
                Assert.True(block.IsCoded);
                Assert.Equal(15, block.Residual.MaximumCoefficients);
                Assert.Equal(0, block.Residual.TotalCoefficients);
            });
        }
        Assert.Equal(0, bits.RemainingBits);
    }

    [Theory]
    [InlineData(1, 4, 2)]
    [InlineData(2, 8, 4)]
    public void Read_ShouldDecodeChromaDcOnlyWhenCodedPatternIsOne(
        uint chromaFormat, int expectedDcCoefficients, int expectedBlocksHigh)
    {
        string dcZeroToken = chromaFormat == 1 ? "01" : "1";
        H264BitReader bits = CreateReader(dcZeroToken + dcZeroToken);
        H264IntraLumaResidualMacroblock result = _reader.Read(
            bits, CreateHeader(), CreateSyntax(chromaFormat, 1),
            CreateSps(chromaFormat), 0, 0, 2,
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext());

        Assert.Equal(2, result.ChromaComponents.Count);
        foreach (H264IntraChromaResidualComponent component in result.ChromaComponents)
        {
            Assert.NotNull(component.Dc);
            Assert.Equal(expectedDcCoefficients, component.Dc.MaximumCoefficients);
            Assert.Equal(2 * expectedBlocksHigh, component.Blocks.Count);
            Assert.All(component.Blocks, block =>
            {
                Assert.False(block.IsCoded);
                Assert.Equal(0, block.Residual.TotalCoefficients);
            });
        }
        Assert.Equal(0, bits.RemainingBits);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    public void Read_ShouldReturnUncodedChromaWhenCodedPatternIsZero(
        uint chromaFormat, int expectedBlocksHigh)
    {
        H264IntraLumaResidualMacroblock result = _reader.Read(
            CreateReader(""), CreateHeader(), CreateSyntax(chromaFormat, 0),
            CreateSps(chromaFormat), 0, 0, 1,
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext());

        Assert.Equal(2, result.ChromaComponents.Count);
        foreach (H264IntraChromaResidualComponent component in result.ChromaComponents)
        {
            Assert.Null(component.Dc);
            Assert.Equal(2 * expectedBlocksHigh, component.Blocks.Count);
            Assert.All(component.Blocks, block => Assert.False(block.IsCoded));
        }
    }

    [Fact]
    public void Read_ShouldRejectChromaWithoutIndependentNeighbourContexts()
    {
        Assert.Throws<ArgumentNullException>(() => _reader.Read(
            CreateReader(""), CreateHeader(), CreateSyntax(1, 0),
            CreateSps(1), 0, 0, 0, new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectReusedNeighbourContextAcrossComponents()
    {
        H264CavlcNeighbourContext shared = new();
        Assert.Throws<ArgumentException>(() => _reader.Read(
            CreateReader(""), CreateHeader(), CreateSyntax(1, 0),
            CreateSps(1), 0, 0, 0, shared, shared, new H264CavlcNeighbourContext()));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Read_ShouldRejectUnsupportedChromaFormats(uint chromaFormat)
    {
        Assert.Throws<NotSupportedException>(() => _reader.Read(
            CreateReader(""), CreateHeader(), CreateSyntax(chromaFormat, 0),
            CreateSps(chromaFormat), 0, 0, 0,
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectSeparateColourPlane()
    {
        Assert.Throws<NotSupportedException>(() => _reader.Read(
            CreateReader(""), CreateHeader(), CreateSyntax(1, 0),
            CreateSps(1) with { SeparateColourPlaneFlag = true },
            0, 0, 0, new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectChromaPatternOutsideNormativeRange()
    {
        Assert.Throws<FormatException>(() => _reader.Read(
            CreateReader(""), CreateHeader(), CreateSyntax(1, 3),
            CreateSps(1), 0, 0, 0,
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext()));
    }

    [Fact]
    public void Read_ShouldRejectChromaArrayTypeMismatch()
    {
        Assert.Throws<FormatException>(() => _reader.Read(
            CreateReader(""), CreateHeader(), CreateSyntax(2, 0),
            CreateSps(1), 0, 0, 0,
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext(),
            new H264CavlcNeighbourContext()));
    }

    private static H264MacroblockHeader CreateHeader() =>
        new(0, 0, false, 0, true, false, null, null, null, false);

    private static H264IntraMacroblockSyntax CreateSyntax(uint chromaFormat, uint chromaPattern) =>
        new(null, null, 0,
            new H264CodedBlockPattern(0, 0, chromaPattern, chromaFormat), null);

    private static H264SequenceParameterSet CreateSps(uint chromaFormat) =>
        new(0, 100, 40, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264BitReader CreateReader(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1')
                bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}
