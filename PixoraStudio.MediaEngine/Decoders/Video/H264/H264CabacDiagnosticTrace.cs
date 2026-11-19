namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Optional diagnostic channel for annotating CABAC bin traces with the
/// syntax element currently being decoded. Production readers need not
/// implement this interface; it has no effect on bitstream decoding.
/// </summary>
public interface IH264CabacDiagnosticTraceSink
{
    void TraceDiagnostic(string marker);
}

/// <summary>Emits diagnostic markers only when the supplied reader opts in.</summary>
internal static class H264CabacDiagnosticTrace
{
    public static void Mark(IH264CabacBinReader reader, string marker)
    {
        if (reader is IH264CabacDiagnosticTraceSink sink)
            sink.TraceDiagnostic(marker);
    }
}
