using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

/// <summary>Verifies bounded, immutable snapshots of production CABAC trace markers.</summary>
public sealed class H264CabacDiagnosticTraceTests
{
    [Fact]
    public void TraceDiagnostic_ShouldRetainOnlyTheMostRecent128Markers()
    {
        H264CabacSliceDataReader reader = new(
            new H264BitReader(new byte[] { 0x00, 0x00 }), 26, false, 0);

        for (int index = 0; index < 140; index++)
            reader.TraceDiagnostic($"marker-{index}");

        Assert.Equal(128, reader.DiagnosticTrace.Count);
        Assert.Equal("marker-12", reader.DiagnosticTrace[0]);
        Assert.Equal("marker-139", reader.DiagnosticTrace[^1]);
    }

    [Fact]
    public void DiagnosticTrace_ShouldReturnSnapshotRatherThanMutableInternalCollection()
    {
        H264CabacSliceDataReader reader = new(
            new H264BitReader(new byte[] { 0x00, 0x00 }), 26, false, 0);
        reader.TraceDiagnostic("initial");

        IReadOnlyList<string> snapshot = reader.DiagnosticTrace;
        reader.TraceDiagnostic("later");

        Assert.Equal(new[] { "initial" }, snapshot);
        Assert.Equal(new[] { "initial", "later" }, reader.DiagnosticTrace);
    }
}
