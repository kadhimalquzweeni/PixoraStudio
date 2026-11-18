namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Predicted luma and chroma samples for one inter partition. Plane buffers
/// are tightly packed in raster order; chroma dimensions follow the source
/// picture's 4:2:0 or 4:2:2 format.
/// </summary>
public sealed record H264MotionCompensatedBlock(
    int LumaWidth,
    int LumaHeight,
    int ChromaWidth,
    int ChromaHeight,
    byte[] Luma,
    byte[] Cb,
    byte[] Cr);
