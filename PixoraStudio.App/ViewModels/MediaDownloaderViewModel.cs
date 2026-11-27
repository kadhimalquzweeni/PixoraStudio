using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PixoraStudio.App.ViewModels;

public partial class MediaDownloaderViewModel : ObservableObject
{
    private static readonly (string Platform, string[] Domains)[] SupportedPlatforms =
    [
        ("YouTube", ["youtube.com", "youtu.be", "youtube-nocookie.com"]),
        ("TikTok", ["tiktok.com"]),
        ("Instagram", ["instagram.com"]),
        ("Facebook", ["facebook.com", "fb.watch"]),
        ("X", ["x.com", "twitter.com"]),
        ("Reddit", ["reddit.com", "redd.it"]),
    ];

    [ObservableProperty]
    private string url = string.Empty;

    [ObservableProperty]
    private string outputFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "PixoraStudio Downloads");

    [ObservableProperty]
    private string selectedFormat = "Video with audio";

    [ObservableProperty]
    private string selectedQuality = "Best available";

    [ObservableProperty]
    private string statusMessage = "Paste a video link to get started.";

    [ObservableProperty]
    private string detectedPlatform = "No link checked";

    [ObservableProperty]
    private bool isLinkRecognised;

    partial void OnUrlChanged(string value)
    {
        IsLinkRecognised = false;
        DetectedPlatform = "No link checked";
        StatusMessage = string.IsNullOrWhiteSpace(value)
            ? "Paste a video link to get started."
            : "Check the link to identify its source.";
    }

    [RelayCommand]
    private void CheckLink()
    {
        IsLinkRecognised = false;
        DetectedPlatform = "Unsupported link";
        if (!Uri.TryCreate(Url?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            StatusMessage = "Enter a valid http or https video URL.";
            return;
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        foreach (var platform in SupportedPlatforms)
        {
            if (platform.Domains.Any(domain =>
                    host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
            {
                DetectedPlatform = platform.Platform;
                IsLinkRecognised = true;
                StatusMessage = $"{platform.Platform} link recognised. Downloader support for this source is being connected.";
                return;
            }
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (new[] { ".mp4", ".webm", ".mov", ".mkv", ".m3u8", ".mpd" }
            .Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            DetectedPlatform = "Direct video link";
            IsLinkRecognised = true;
            StatusMessage = "Direct video link recognised. Downloader support is being connected.";
            return;
        }

        StatusMessage = "This link is not from a currently recognised source.";
    }

    [RelayCommand]
    private void ClearLink()
    {
        Url = string.Empty;
    }
}
