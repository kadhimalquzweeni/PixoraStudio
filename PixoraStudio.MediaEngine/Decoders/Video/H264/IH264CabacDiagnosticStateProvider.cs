namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Optional read-only arithmetic/context snapshot exposed for CABAC diagnostics.
/// It does not participate in bin decoding or context adaptation.
/// </summary>
public interface IH264CabacDiagnosticStateProvider
{
    int ArithmeticRange { get; }

    int ArithmeticOffset { get; }

    int BitPosition { get; }

    H264CabacContextModel GetContextModel(int contextIndex);
}
