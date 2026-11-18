using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacContextInitializerTests
{
    [Fact]
    public void TryInitialize_ShouldApplyIntraSliceInitialisationFormula()
    {
        bool initialized = H264CabacContextInitializer.TryInitialize(
            contextIndex: 0, sliceQpY: 26, isIntraSlice: true, cabacInitIdc: 0,
            out H264CabacContextModel context);

        Assert.True(initialized);
        Assert.Equal(new H264CabacContextModel(46, 0), context);
    }

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 4, 0)]
    public void TryInitialize_ShouldSelectPOrBSliceInitialisationSet(
        int cabacInitIdc, int expectedState, int expectedMps)
    {
        bool initialized = H264CabacContextInitializer.TryInitialize(
            contextIndex: 54, sliceQpY: 26, isIntraSlice: false, cabacInitIdc,
            out H264CabacContextModel context);

        Assert.True(initialized);
        Assert.Equal(new H264CabacContextModel(expectedState, expectedMps), context);
    }

    [Fact]
    public void TryInitialize_ShouldClipPreContextStateToOneAnd126()
    {
        Assert.True(H264CabacContextInitializer.TryInitialize(
            0, 0, true, 0, out H264CabacContextModel lower));
        Assert.Equal(new H264CabacContextModel(62, 0), lower);

        Assert.True(H264CabacContextInitializer.TryInitialize(
            422, 51, true, 0, out H264CabacContextModel upper));
        Assert.Equal(new H264CabacContextModel(62, 1), upper);
    }

    [Fact]
    public void TryInitialize_ShouldReturnFalseForTerminateContext()
    {
        Assert.False(H264CabacContextInitializer.TryInitialize(
            276, 26, true, 0, out _));
    }

    [Fact]
    public void TryInitialize_ShouldReturnFalseForUnavailableContextInSliceClass()
    {
        Assert.False(H264CabacContextInitializer.TryInitialize(
            54, 26, true, 0, out _));
    }

    [Theory]
    [InlineData(-1, 26, true, 0)]
    [InlineData(460, 26, true, 0)]
    [InlineData(0, -1, true, 0)]
    [InlineData(0, 52, true, 0)]
    [InlineData(0, 26, false, -1)]
    [InlineData(0, 26, false, 3)]
    public void TryInitialize_ShouldRejectOutOfRangeArguments(
        int contextIndex, int qp, bool intra, int initIdc)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264CabacContextInitializer.TryInitialize(
                contextIndex, qp, intra, initIdc, out _));
    }
}
