using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;

namespace PixoraStudio.Infrastructure.Services;

public class MediaTypeDetector : IMediaTypeDetector
{
    private static readonly HashSet<string> ImageExtensions =
    [
        ".jpg", ".jpeg", ".png", ".bmp",
        ".webp", ".gif", ".tiff", ".tif"
    ];

    private static readonly HashSet<string> VideoExtensions =
    [
        ".mp4", ".mov", ".avi", ".mkv",
        ".webm", ".wmv", ".m4v"
    ];

    private static readonly HashSet<string> AudioExtensions =
    [
        ".mp3", ".wav", ".flac", ".aac", ".ogg"
    ];

    public MediaType Detect(string filePath)
    {
        string extension = Path.GetExtension(filePath)
            .ToLowerInvariant();

        if (ImageExtensions.Contains(extension))
            return MediaType.Image;

        if (VideoExtensions.Contains(extension))
            return MediaType.Video;

        if (AudioExtensions.Contains(extension))
            return MediaType.Audio;

        if (extension == ".pixora")
            return MediaType.PixoraProject;

        return MediaType.Unknown;
    }
}
