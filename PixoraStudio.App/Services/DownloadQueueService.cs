using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PixoraStudio.App.Services;

public enum DownloadQueueItemStatus
{
    Queued,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public sealed class DownloadQueueItem : ObservableObject
{
    private DownloadQueueItemStatus _status;
    private double _progress;
    private string? _outputPath;
    private string? _errorMessage;

    internal DownloadQueueItem(Uri source, string outputDirectory)
    {
        Source = source;
        OutputDirectory = outputDirectory;
    }

    public Uri Source { get; }
    public string OutputDirectory { get; }
    public string DisplayName => Path.GetFileName(Source.LocalPath) is { Length: > 0 } name
        ? name
        : Source.Host;

    public DownloadQueueItemStatus Status
    {
        get => _status;
        internal set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanRetry));
                OnPropertyChanged(nameof(CanRemove));
                OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    public double Progress
    {
        get => _progress;
        internal set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    public string? OutputPath
    {
        get => _outputPath;
        internal set => SetProperty(ref _outputPath, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set => SetProperty(ref _errorMessage, value);
    }

    public string StatusText => Status switch
    {
        DownloadQueueItemStatus.Queued => "Queued",
        DownloadQueueItemStatus.Downloading => "Downloading",
        DownloadQueueItemStatus.Completed => "Completed",
        DownloadQueueItemStatus.Failed => "Failed",
        DownloadQueueItemStatus.Cancelled => "Cancelled",
        _ => "Unknown"
    };

    public string ProgressText => double.IsNaN(Progress) ? "Downloading..." : $"{Progress:0}%";
    public bool IsProgressIndeterminate => Status == DownloadQueueItemStatus.Downloading && double.IsNaN(Progress);
    public bool CanCancel => Status is DownloadQueueItemStatus.Queued or DownloadQueueItemStatus.Downloading;
    public bool CanRetry => Status is DownloadQueueItemStatus.Failed or DownloadQueueItemStatus.Cancelled;
    public bool CanRemove => Status != DownloadQueueItemStatus.Downloading;

    internal CancellationTokenSource? Cancellation { get; set; }
}

public sealed class DownloadQueueService(IDirectMediaDownloadService downloadService) : ObservableObject
{
    private Task? _processingTask;
    private bool _isProcessing;

    public ObservableCollection<DownloadQueueItem> Items { get; } = [];

    public bool IsProcessing
    {
        get => _isProcessing;
        private set => SetProperty(ref _isProcessing, value);
    }

    public DownloadQueueItem Add(Uri source, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (source.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only HTTP and HTTPS URLs are supported.", nameof(source));

        if (!DirectMediaFormats.IsSupportedExtension(Path.GetExtension(source.AbsolutePath)))
            throw new NotSupportedException("Only direct MP4, WebM, MOV and MKV files can be queued.");

        var existing = Items.FirstOrDefault(item =>
            item.Source == source &&
            string.Equals(item.OutputDirectory, outputDirectory, StringComparison.OrdinalIgnoreCase) &&
            item.Status is DownloadQueueItemStatus.Queued or DownloadQueueItemStatus.Downloading);

        if (existing is not null)
            return existing;

        var item = new DownloadQueueItem(source, outputDirectory);
        Items.Add(item);
        return item;
    }

    public Task StartAsync()
    {
        if (_processingTask is { IsCompleted: false })
            return _processingTask;

        _processingTask = ProcessQueueAsync();
        return _processingTask;
    }

    public void Cancel(DownloadQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Items.Contains(item))
            return;

        if (item.Status == DownloadQueueItemStatus.Queued)
            item.Status = DownloadQueueItemStatus.Cancelled;
        else if (item.Status == DownloadQueueItemStatus.Downloading)
            item.Cancellation?.Cancel();
    }

    public bool Retry(DownloadQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Items.Contains(item) || !item.CanRetry)
            return false;

        item.Progress = 0;
        item.OutputPath = null;
        item.ErrorMessage = null;
        item.Status = DownloadQueueItemStatus.Queued;
        return true;
    }

    public bool Remove(DownloadQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.CanRemove && Items.Remove(item);
    }

    public void ClearCompleted()
    {
        foreach (var item in Items.Where(item => item.Status == DownloadQueueItemStatus.Completed).ToArray())
            Items.Remove(item);
    }

    private async Task ProcessQueueAsync()
    {
        IsProcessing = true;
        try
        {
            while (Items.FirstOrDefault(item => item.Status == DownloadQueueItemStatus.Queued) is { } item)
            {
                using var cancellation = new CancellationTokenSource();
                item.Cancellation = cancellation;
                item.Status = DownloadQueueItemStatus.Downloading;
                item.Progress = 0;
                item.ErrorMessage = null;

                try
                {
                    var progress = new Progress<double>(value => item.Progress = value);
                    item.OutputPath = await downloadService.DownloadAsync(
                        item.Source,
                        item.OutputDirectory,
                        progress,
                        cancellation.Token);
                    item.Progress = 100;
                    item.Status = DownloadQueueItemStatus.Completed;
                }
                catch (OperationCanceledException)
                {
                    item.Status = DownloadQueueItemStatus.Cancelled;
                }
                catch (Exception exception)
                {
                    item.ErrorMessage = exception.Message;
                    item.Status = DownloadQueueItemStatus.Failed;
                }
                finally
                {
                    item.Cancellation = null;
                }
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
