using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixoraStudio.App.Services;

namespace PixoraStudio.App.ViewModels;

public partial class MediaDownloaderViewModel(IDirectMediaDownloadService downloadService) : ObservableObject
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
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
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
    private string statusMessage = "Paste a direct video file link to get started.";

    [ObservableProperty]
    private string detectedPlatform = "No link checked";

    [ObservableProperty]
    private string? downloadedFilePath;

    [ObservableProperty]
    private double downloadProgress;

    [ObservableProperty]
    private bool isLinkRecognised;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    private bool isDownloading;

    public string[] FormatOptions { get; } = ["Video with audio", "Video only", "Audio only"];
    public string[] QualityOptions { get; } = ["Best available", "2160p (4K)", "1440p", "1080p", "720p", "480p", "360p"];

    private CancellationTokenSource? _downloadCancellation;

    partial void OnUrlChanged(string value)
    {
        IsLinkRecognised = false;
        DetectedPlatform = "No link checked";
        DownloadedFilePath = null;
        StatusMessage = string.IsNullOrWhiteSpace(value)
            ? "Paste a direct video file link to get started."
            : "Check the link to identify its source.";
    }

    private bool CanDownload() =>
        IsLinkRecognised && !IsDownloading &&
        Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var uri) &&
        DirectMediaFormats.IsSupportedExtension(Path.GetExtension(uri.AbsolutePath));

    [RelayCommand]
    private void CheckLink()
    {
        IsLinkRecognised = false;
        DownloadedFilePath = null;
        if (!Uri.TryCreate(Url?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            DetectedPlatform = "Invalid link";
            StatusMessage = "Enter a valid http or https video URL.";
            DownloadCommand.NotifyCanExecuteChanged();
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
                StatusMessage = $"{platform.Platform} page links are recognised, but their custom extractor has not been implemented yet. Try a direct video file URL for now.";
                DownloadCommand.NotifyCanExecuteChanged();
                return;
            }
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (DirectMediaFormats.IsSupportedExtension(extension))
        {
            DetectedPlatform = "Direct video file";
            IsLinkRecognised = true;
            StatusMessage = "Direct video file recognised. Ready to download.";
        }
        else if (extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".mpd", StringComparison.OrdinalIgnoreCase))
        {
            DetectedPlatform = "Streaming manifest";
            StatusMessage = "HLS and DASH streams need a separate native parser and are not supported yet.";
        }
        else
        {
            DetectedPlatform = "Unsupported link";
            StatusMessage = "This URL is not a supported direct video file.";
        }

        DownloadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (!Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var source))
            return;

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadedFilePath = null;
        StatusMessage = "Connecting to the video source...";
        _downloadCancellation = new CancellationTokenSource();
        DownloadCommand.NotifyCanExecuteChanged();

        var progress = new Progress<double>(value =>\n        {\n            if (double.IsNaN(value))\n            {\n                ProgressDisplayText = "Downloading...";\n                return;\n            }\n\n            DownloadProgress = value;\n            ProgressDisplayText = $"{value:0}%";\n        });
        try
        {
            var path = await downloadService.DownloadAsync(
                source, OutputFolder, progress, _downloadCancellation.Token);
            DownloadedFilePath = path;
            StatusMessage = $"Download complete: {Path.GetFileName(path)}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";
        }
        catch (HttpRequestException exception)
        {
            StatusMessage = $"Download failed: {exception.Message}";
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            StatusMessage = $"Download failed: {exception.Message}";
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            IsDownloading = false;
            DownloadCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCancelDownload() => IsDownloading;

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload() => _downloadCancellation?.Cancel();

    [RelayCommand]
    private void ClearLink()
    {
        Url = string.Empty;
        DownloadProgress = 0;
        DownloadedFilePath = null;
    }
}
