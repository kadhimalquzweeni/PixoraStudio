namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Slice-scoped CABAC reader contract used by macroblock traversal. The
/// production implementation owns the arithmetic engine; the interface also
/// allows deterministic syntax-level slice tests.
/// </summary>
public interface IH264CabacSliceDataReader : IH264CabacBinReader
{
    bool IsTerminated { get; }
    void RestartAfterPcm();
}
