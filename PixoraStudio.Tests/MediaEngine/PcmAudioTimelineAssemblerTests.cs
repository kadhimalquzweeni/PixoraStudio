using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class PcmAudioTimelineAssemblerTests
{
    private static readonly PcmAudioFormat Format = new(48000, 2);

    [Fact]
    public void Append_ShouldJoinContiguousBlocksWithoutChangingSamples()
    {
        PcmAudioTimelineAssembler assembler = new(Format);
        assembler.Append(CreateBuffer(1, 2, 3, 4), 0);
        assembler.Append(CreateBuffer(5, 6, 7, 8), 2);

        PcmAudioBuffer result = assembler.Build();

        Assert.Equal(4, result.FrameCount);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6, 7, 8 }, result.ToArray());
    }

    [Fact]
    public void Append_ShouldInsertSilenceForPresentationGap()
    {
        PcmAudioTimelineAssembler assembler = new(Format);
        assembler.Append(CreateBuffer(1, 2), 0);
        assembler.Append(CreateBuffer(3, 4), 3);

        PcmAudioBuffer result = assembler.Build();

        Assert.Equal(4, result.FrameCount);
        Assert.Equal(
            new float[] { 1, 2, 0, 0, 0, 0, 3, 4 },
            result.ToArray());
    }

    [Fact]
    public void Append_ShouldDiscardFramesAlreadyCoveredByEarlierBlock()
    {
        PcmAudioTimelineAssembler assembler = new(Format);
        assembler.Append(CreateBuffer(1, 2, 3, 4), 0);
        assembler.Append(CreateBuffer(30, 40, 50, 60), 1);

        PcmAudioBuffer result = assembler.Build();

        Assert.Equal(3, result.FrameCount);
        Assert.Equal(new float[] { 1, 2, 3, 4, 50, 60 }, result.ToArray());
    }

    [Fact]
    public void Append_ShouldTrimNegativeDecoderPreroll()
    {
        PcmAudioTimelineAssembler assembler = new(Format);
        assembler.Append(CreateBuffer(1, 2, 3, 4), -1);

        PcmAudioBuffer result = assembler.Build();

        Assert.Equal(1, result.FrameCount);
        Assert.Equal(new float[] { 3, 4 }, result.ToArray());
    }

    [Fact]
    public void Append_ShouldIgnoreBlockFullyCoveredByExistingTimeline()
    {
        PcmAudioTimelineAssembler assembler = new(Format);
        assembler.Append(CreateBuffer(1, 2, 3, 4), 0);
        assembler.Append(CreateBuffer(9, 10), 0);

        PcmAudioBuffer result = assembler.Build();

        Assert.Equal(new float[] { 1, 2, 3, 4 }, result.ToArray());
    }

    [Fact]
    public void Append_ShouldRejectDifferentPcmFormat()
    {
        PcmAudioTimelineAssembler assembler = new(Format);
        PcmAudioBuffer incompatible = new(
            new PcmAudioFormat(44100, 2),
            new float[] { 1, 2 });

        Assert.Throws<ArgumentException>(() => assembler.Append(incompatible, 0));
    }

    [Fact]
    public void Reset_ShouldClearTimeline()
    {
        PcmAudioTimelineAssembler assembler = new(Format);
        assembler.Append(CreateBuffer(1, 2), 0);

        assembler.Reset();

        Assert.Equal(0, assembler.WrittenFrames);
        Assert.Empty(assembler.Build().Samples.ToArray());
    }

    private static PcmAudioBuffer CreateBuffer(params float[] samples) =>
        new(Format, samples);
}
