using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PixoraStudio.Infrastructure.Services;
using System.Threading;

namespace PixoraStudio.App.ViewModels;

public partial class VideoEditorViewModel : ObservableObject
{
    private readonly IVideoProcessingService _videoService;

    private VideoInfo? _videoInfo;
    private readonly FFmpegService _ffmpegService;
    private CancellationTokenSource? _exportCancellationTokenSource;

    public VideoEditorViewModel(
        IVideoProcessingService videoService,
        FFmpegService ffmpegService)
    {
        _videoService = videoService;
        _ffmpegService = ffmpegService;
    }

    [ObservableProperty]
    private string? videoPath;

    [ObservableProperty]
    private string videoName = "No video loaded";

    [ObservableProperty]
    private string videoDuration = "00:00";

    [ObservableProperty]
    private string videoResolution = "-";

    [ObservableProperty]
    private string videoFrameRate = "-";

    [ObservableProperty]
    private BitmapImage? thumbnail;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = "Open a video to get started";

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [RelayCommand]
    private async Task OpenVideoAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Video",
            Filter = "Video Files|*.mp4;*.mov;*.avi;*.mkv;*.wmv;*.webm|" +
                     "All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadVideoAsync(dialog.FileName);
        }
    }

    public async Task LoadVideoAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            ErrorMessage = "The selected video file does not exist.";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            StatusMessage = "Reading video information...";

            VideoPath = filePath;
            VideoName = Path.GetFileName(filePath);
            VideoLoaded?.Invoke(filePath);

            _videoInfo = await _videoService.GetVideoInfoAsync(filePath);
            StatusMessage = "Generating video thumbnail...";

            VideoDuration = FormatDuration(_videoInfo.Duration);
            TotalMilliseconds = _videoInfo.Duration.TotalMilliseconds;
            PlaybackPosition = 0;
            CurrentTime = "00:00";

            TrimStart = 0;
            TrimEnd = TotalMilliseconds;

            VideoResolution =
                $"{_videoInfo.Width} × {_videoInfo.Height}";

            VideoFrameRate =
                $"{_videoInfo.FramesPerSecond:F2} FPS";

            string thumbnailPath = Path.Combine(
                Path.GetTempPath(),
                $"Pixora_{Guid.NewGuid()}.png");

            await _videoService.GenerateThumbnailAsync(
                filePath,
                TimeSpan.Zero,
                thumbnailPath);

            Thumbnail = new BitmapImage();

            Thumbnail.BeginInit();
            Thumbnail.UriSource = new Uri(thumbnailPath);
            Thumbnail.CacheOption = BitmapCacheOption.OnLoad;
            Thumbnail.EndInit();
            Thumbnail.Freeze();

            StatusMessage = "Video loaded successfully";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load video: {ex.Message}";
            StatusMessage = "Unable to load video";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    [RelayCommand]
    private void CloseVideo()
    {
        _exportCancellationTokenSource?.Cancel();
        VideoPath = null;
        VideoName = "No video loaded";
        VideoDuration = "00:00";
        VideoResolution = "-";
        VideoFrameRate = "-";
        Thumbnail = null;
        _videoInfo = null;
        ErrorMessage = string.Empty;
        StatusMessage = "Open a video to get started";
    }


    public event Action<string>? VideoLoaded;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private double playbackPosition;

    [ObservableProperty]
    private double totalMilliseconds;

    [ObservableProperty]
    private string currentTime = "00:00";

    [ObservableProperty]
    private double volume = 100;

    public event Action? PlayPauseRequested;
    public event Action<double>? SeekRequested;
    public event Action<double>? VolumeChanged;

    [RelayCommand]
    private void PlayPause()
    {
        PlayPauseRequested?.Invoke();
    }

    [RelayCommand]
    private void Seek(double position)
    {
        SeekRequested?.Invoke(position);
    }

    [RelayCommand]
    private void ChangeVolume(double value)
    {
        Volume = value;
        VolumeChanged?.Invoke(value);
    }


    [ObservableProperty]
    private double trimStart;

    [ObservableProperty]
    private double trimEnd;

    public double TrimDuration => Math.Max(0, TrimEnd - TrimStart);

    public string TrimStartTime =>
        FormatDuration(TimeSpan.FromMilliseconds(TrimStart));

    public string TrimEndTime =>
        FormatDuration(TimeSpan.FromMilliseconds(TrimEnd));

    public string TrimDurationTime =>
        FormatDuration(TimeSpan.FromMilliseconds(TrimDuration));

    partial void OnTrimStartChanged(double value)
    {
        if (TrimStart > TrimEnd)
        {
            TrimEnd = TrimStart;
        }

        OnPropertyChanged(nameof(TrimDuration));
        OnPropertyChanged(nameof(TrimStartTime));
        OnPropertyChanged(nameof(TrimEndTime));
        OnPropertyChanged(nameof(TrimDurationTime));
    }

    partial void OnTrimEndChanged(double value)
    {
        if (TrimEnd < TrimStart)
        {
            TrimStart = TrimEnd;
        }

        OnPropertyChanged(nameof(TrimDuration));
        OnPropertyChanged(nameof(TrimStartTime));
        OnPropertyChanged(nameof(TrimEndTime));
        OnPropertyChanged(nameof(TrimDurationTime));
    }

    [RelayCommand]
    private void ResetTrim()
    {
        TrimStart = 0;
        TrimEnd = TotalMilliseconds;
    }

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private double exportProgress;

    [ObservableProperty]
    private string exportStatus = string.Empty;

    [RelayCommand]
    private async Task ExportTrimmedVideoAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoPath) ||
            !File.Exists(VideoPath))
        {
            ErrorMessage = "Please open a video before exporting.";
            return;
        }

        if (TrimEnd <= TrimStart)
        {
            ErrorMessage = "The trim end must be after the trim start.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Trimmed Video",
            Filter = "MP4 Video|*.mp4",
            FileName = $"{Path.GetFileNameWithoutExtension(VideoPath)}_trimmed.mp4",
            DefaultExt = ".mp4",
            AddExtension = true
        };

        if (dialog.ShowDialog() != true)
            return;

        _exportCancellationTokenSource = new CancellationTokenSource();

        try
        {
            IsExporting = true;
            ExportProgress = 0;
            ErrorMessage = string.Empty;
            ExportStatus = "Preparing video export...";

            StatusMessage = "Exporting trimmed video...";

            var progress = new Progress<double>(value =>
            {
                ExportProgress = value;
                ExportStatus = $"Exporting video... {value:F1}%";
            });

            await _ffmpegService.ExportTrimmedVideoAsync(
                VideoPath,
                dialog.FileName,
                TimeSpan.FromMilliseconds(TrimStart),
                TimeSpan.FromMilliseconds(TrimEnd),
                progress,
                _exportCancellationTokenSource.Token);

            ExportProgress = 100;
            ExportStatus = "Export completed successfully!";
            StatusMessage = "Trimmed video exported successfully.";
        }
        catch (OperationCanceledException)
        {
            ExportStatus = "Export cancelled.";
            StatusMessage = "Video export was cancelled.";
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ExportStatus = "Export failed.";
            ErrorMessage = ex.Message;
            StatusMessage = "Unable to export trimmed video.";
        }
        finally
        {
            IsExporting = false;

            _exportCancellationTokenSource?.Dispose();
            _exportCancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void CancelExport()
    {
        _exportCancellationTokenSource?.Cancel();
    }

}