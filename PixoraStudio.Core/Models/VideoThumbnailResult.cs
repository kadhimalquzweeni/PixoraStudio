namespace PixoraStudio.Core.Models;

/// <summary>Identifies a thumbnail written during a batched filmstrip extraction.</summary>
public sealed record VideoThumbnailResult(int Index, string OutputPath);
