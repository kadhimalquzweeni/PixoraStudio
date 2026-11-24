using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

/// <summary>Verifies bounded, immutable snapshots of production CABAC trace markers.</summary>
public sealed class H264CabacDiagnosticTraceTests
{
    [Fact]
    public void TraceDiagnostic_ShouldBeDisabledByDefault()
    {
        H264CabacSliceDataReader reader = new(
            new H264BitReader(new byte[] { 0x00, 0x00 }), 26, false, 0);

        reader.TraceDiagnostic("should-not-be-retained");

        Assert.False(reader.IsDiagnosticTraceEnabled);
        Assert.Empty(reader.DiagnosticTrace);
    }

    [Fact]
    public void TraceDiagnostic_ShouldBeCollectedWhenExplicitlyEnabled()
    {
        H264CabacSliceDataReader reader = new(
            new H264BitReader(new byte[] { 0x00, 0x00 }), 26, false, 0,
            enableDiagnosticTrace: true);

        reader.TraceDiagnostic("enabled");

        Assert.True(reader.IsDiagnosticTraceEnabled);
        Assert.Equal(new[] { "enabled" }, reader.DiagnosticTrace);
    }

    [Fact]
    public void TraceDiagnostic_ShouldPreserveInitialAndMostRecent256Markers()
    {
        H264CabacSliceDataReader reader = new(
            new H264BitReader(new byte[] { 0x00, 0x00 }), 26, false, 0, enableDiagnosticTrace: true);

        for (int index = 0; index < 600; index++)
            reader.TraceDiagnostic($"marker-{index}");

        Assert.Equal(512, reader.DiagnosticTrace.Count);
        Assert.Equal("marker-0", reader.DiagnosticTrace[0]);
        Assert.Equal("marker-255", reader.DiagnosticTrace[255]);
        Assert.Equal("marker-344", reader.DiagnosticTrace[256]);
        Assert.Equal("marker-599", reader.DiagnosticTrace[^1]);
    }

    [Fact]
    public void DiagnosticTrace_ShouldReturnSnapshotRatherThanMutableInternalCollection()
    {
        H264CabacSliceDataReader reader = new(
            new H264BitReader(new byte[] { 0x00, 0x00 }), 26, false, 0, enableDiagnosticTrace: true);
        reader.TraceDiagnostic("initial");

        IReadOnlyList<string> snapshot = reader.DiagnosticTrace;
        reader.TraceDiagnostic("later");

        Assert.Equal(new[] { "initial" }, snapshot);
        Assert.Equal(new[] { "initial", "later" }, reader.DiagnosticTrace);
    }
}
