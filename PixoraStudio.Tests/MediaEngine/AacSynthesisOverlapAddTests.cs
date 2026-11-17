using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacSynthesisOverlapAddTests
{
    [Fact]
    public void Process_ShouldEmitCurrentFirstHalfAndRetainSecondHalf()
    {
        AacSynthesisOverlapAdd processor = new();
        float[] block = Enumerable.Range(0, 2048).Select(value => (float)value).ToArray();

        float[] output = processor.Process(block);

        Assert.Equal(1024, output.Length);
        Assert.Equal(0f, output[0]);
        Assert.Equal(1023f, output[1023]);

        float[] next = processor.Process(new float[2048]);

        Assert.Equal(1024f, next[0]);
        Assert.Equal(2047f, next[1023]);
    }

    [Fact]
    public void Reset_ShouldClearPreviousOverlap()
    {
        AacSynthesisOverlapAdd processor = new();
        processor.Process(Enumerable.Repeat(1f, 2048).ToArray());

        processor.Reset();

        float[] output = processor.Process(new float[2048]);

        Assert.All(output, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void Process_ShouldRejectWrongBlockLength()
    {
        Assert.Throws<ArgumentException>(
            () => new AacSynthesisOverlapAdd().Process(new float[1024]));
    }
}
