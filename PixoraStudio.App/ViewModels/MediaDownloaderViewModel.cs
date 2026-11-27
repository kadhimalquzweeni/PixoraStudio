using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixoraStudio.App.Services;

namespace PixoraStudio.App.ViewModels;

public partial class MediaDownloaderViewModel : ObservableObject
{
    private readonly DownloadQueueService _queueService;

    private static readonly (string Platform, string[] Domains)[] SupportedPlatforms =
    [
        ("YouTube", ["youtube.com", "youtu.be", "youtube-nocookie.com"]),
        ("TikTok", ["tiktok.com"]),
        ("Instagram", ["instagram.com"]),
        ("Facebook", ["facebook.com", "fb.watch"]),
        ("X", ["x.com", "twitter.com"]),
        ("Reddit", ["reddit.com", "redd.it"]),
    ];

    public MediaDownloaderViewModel(DownloadQueueService queueService)
    {
        _queueService = queueService;
        _queueService.Items.CollectionChanged += OnQueueItemsChanged;
        _queueService.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsQueueProcessing));
        UpdateQueueCount();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToQueueCommand))]
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
    private bool isLinkRecognised;

    [ObservableProperty]
    private string queueCountText = "0 downloads";

    public string[] FormatOptions { get; } = ["Video with audio", "Video only", "Audio only"];
    public string[] QualityOptions { get; } = ["Best available", "2160p (4K)", "1440p", "1080p", "720p", "480p", "360p"];
    public System.Collections.ObjectModel.ObservableCollection<DownloadQueueItem> QueueItems => _queueService.Items;
    public bool IsQueueProcessing => _queueService.IsProcessing;

    partial void OnUrlChanged(string value)
    {
        IsLinkRecognised = false;
        DetectedPlatform = "No link checked";
        StatusMessage = string.IsNullOrWhiteSpace(value)
            ? "Paste a direct video file link to get started."
            : "Check the link to identify its source.";
        AddToQueueCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddToQueue() =>
        IsLinkRecognised &&
        Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var uri) &&
        DirectMediaFormats.IsSupportedExtension(Path.GetExtension(uri.AbsolutePath));

    [RelayCommand]
    private void CheckLink()
    {
        IsLinkRecognised = false;
        if (!Uri.TryCreate(Url?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            DetectedPlatform = "Invalid link";
            StatusMessage = "Enter a valid http or https video URL.";
            AddToQueueCommand.NotifyCanExecuteChanged();
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
                AddToQueueCommand.NotifyCanExecuteChanged();
                return;
            }
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (DirectMediaFormats.IsSupportedExtension(extension))
        {
            DetectedPlatform = "Direct video file";
            IsLinkRecognised = true;
            StatusMessage = "Direct video file recognised. Ready to add to the queue.";
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

        AddToQueueCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddToQueue))]
    private void AddToQueue()
    {
        if (!Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var source))
            return;

        try
        {
            var existing = QueueItems.FirstOrDefault(item =>
                item.Source == source &&
                string.Equals(item.OutputDirectory, OutputFolder, StringComparison.OrdinalIgnoreCase) &&
                item.Status is DownloadQueueItemStatus.Queued or DownloadQueueItemStatus.Downloading);

            var item = _queueService.Add(source, OutputFolder);
            StatusMessage = ReferenceEquals(existing, item)
                ? "This link is already in the download queue."
                : "Added to the queue. Download will start automatically.";
            _ = _queueService.StartAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not queue download: {exception.Message}";
        }
    }

    [RelayCommand]
    private void PauseQueueItem(DownloadQueueItem item) => _queueService.Pause(item);

    [RelayCommand]
    private void ResumeQueueItem(DownloadQueueItem item) => _queueService.Resume(item);

    [RelayCommand]
    private void CancelQueueItem(DownloadQueueItem item) => _queueService.Cancel(item);

    [RelayCommand]
    private void RetryQueueItem(DownloadQueueItem item)
    {
        if (_queueService.Retry(item))
            _ = _queueService.StartAsync();
    }

    [RelayCommand]
    private void RemoveQueueItem(DownloadQueueItem item) => _queueService.Remove(item);

    [RelayCommand]
    private void ClearCompleted() => _queueService.ClearCompleted();

    [RelayCommand]
    private void ClearLink() => Url = string.Empty;

    private void OnQueueItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateQueueCount();

    private void UpdateQueueCount()
    {
        var count = QueueItems.Count;
        QueueCountText = count == 1 ? "1 download" : $"{count} downloads";
    }
}
