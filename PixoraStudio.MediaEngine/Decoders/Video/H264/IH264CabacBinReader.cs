namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Bin-level contract consumed by CABAC syntax-element readers.</summary>
public interface IH264CabacBinReader
{
    int DecodeBin(int contextIndex);
    int DecodeBypass();
    int DecodeTerminate();
    void MarkTerminated();
}
