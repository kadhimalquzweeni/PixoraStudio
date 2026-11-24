namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Raw sample planes decoded from one H.264 I_PCM macroblock.</summary>
public sealed record H264PcmMacroblock(
    uint MacroblockAddress,
    IReadOnlyList<byte> LumaSamples,
    IReadOnlyList<byte> CbSamples,
    IReadOnlyList<byte> CrSamples);
