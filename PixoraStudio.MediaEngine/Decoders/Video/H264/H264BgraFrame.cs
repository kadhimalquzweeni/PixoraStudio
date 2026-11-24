namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>32-bit BGRA pixels ready for a desktop or image presentation surface.</summary>
public sealed record H264BgraFrame(
    int Width,
    int Height,
    int Stride,
    byte[] Pixels);
