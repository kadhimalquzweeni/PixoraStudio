using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;

namespace PixoraStudio.App.ViewModels;

public partial class VideoEditorViewModel : ObservableObject
{
    private readonly IVideoProcessingService _videoService;
    private readonly IVideoExportService _videoExportService;
    private CancellationTokenSource? _exportCancellation;

    private VideoInfo? _videoInfo;
    public ObservableCollection<BitmapImage> VideoFrames { get; } = new();

    public VideoEditorViewModel(
        IVideoProcessingService videoService,
        IVideoExportService videoExportService)
    {
        _videoService = videoService;
        _videoExportService = videoExportService;
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

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private double exportProgress;

    [ObservableProperty]
    private string exportStatusMessage = string.Empty;

    private bool CanExportTrimmedVideo() =>
        !IsLoading &&
        !IsExporting &&
        !string.IsNullOrWhiteSpace(VideoPath) &&
        TrimDuration > 0;

    private bool CanCancelExport() => IsExporting;

    partial void OnVideoPathChanged(string? value)
    {
        ExportTrimmedVideoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        ExportTrimmedVideoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExportingChanged(bool value)
    {
        ExportTrimmedVideoCommand.NotifyCanExecuteChanged();
        CancelExportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task OpenVideoAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Video",
            Filter = "MP4 Video Files|*.mp4|All Files|*.*"
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

            // Start LibVLC immediately rather than making video startup wait
            // for managed MP4 metadata parsing. The timeline can render its
            // duration as soon as metadata arrives; Timer_Tick already tracks
            // the player clock while TotalMilliseconds is temporarily zero.
            TotalMilliseconds = 0;
            PlaybackPosition = 0;
            CurrentTime = "00:00";
            TrimStart = 0;
            TrimEnd = 0;
            VideoDuration = "00:00";
            VideoResolution = "-";
            VideoFrameRate = "-";
            VideoFrames.Clear();

            StatusMessage = "Opening video...";
            VideoLoaded?.Invoke(filePath);

            _videoInfo = await _videoService.GetVideoInfoAsync(filePath);

            TotalMilliseconds = Math.Max(0, _videoInfo.Duration.TotalMilliseconds);
            // Do not reset PlaybackPosition or CurrentTime here: playback may
            // already have advanced while the MP4 metadata was being read.
            TrimStart = 0;
            TrimEnd = TotalMilliseconds;

            VideoDuration = FormatDuration(_videoInfo.Duration);
            VideoResolution = $"{_videoInfo.Width} × {_videoInfo.Height}";
            VideoFrameRate = $"{_videoInfo.FramesPerSecond:F2} FPS";

            StatusMessage = "Generating video thumbnails...";

            await GenerateVideoFramesAsync(filePath);

            StatusMessage = VideoFrames.Count > 0
                ? "Video loaded successfully"
                : "Video loaded, but thumbnails could not be generated.";
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
        _exportCancellation?.Cancel();
        VideoClosed?.Invoke();
        VideoPath = null;
        VideoName = "No video loaded";
        VideoDuration = "00:00";
        VideoResolution = "-";
        VideoFrameRate = "-";
        Thumbnail = null;
        _videoInfo = null;
        ErrorMessage = string.Empty;
        StatusMessage = "Open a video to get started";
        VideoFrames.Clear();
    }


    public event Action<string>? VideoLoaded;
    public event Action? VideoClosed;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool canStepFrames;

    [ObservableProperty]
    private double playbackPosition;

    [ObservableProperty]
    private double totalMilliseconds;

    [ObservableProperty]
    private string currentTime = "00:00";

    [ObservableProperty]
    private double volume = 100;

    [ObservableProperty]
    private bool isMuted;

    public string MuteButtonText => IsMuted ? "Unmute" : "Mute";

    public event Action? PlayPauseRequested;
    public event Action<int>? FrameStepRequested;
    public event Action<double>? SeekRequested;
    public event Action<double>? VolumeChanged;
    public event Action<bool>? MuteChanged;

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(MuteButtonText));
        MuteChanged?.Invoke(value);
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void PlayPause()
    {
        PlayPauseRequested?.Invoke();
    }

    [RelayCommand]
    private void PreviousFrame()
    {
        if (CanStepFrames)
            FrameStepRequested?.Invoke(-1);
    }

    [RelayCommand]
    private void NextFrame()
    {
        if (CanStepFrames)
            FrameStepRequested?.Invoke(1);
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
        ExportTrimmedVideoCommand.NotifyCanExecuteChanged();
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
        ExportTrimmedVideoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ResetTrim()
    {
        TrimStart = 0;
        TrimEnd = TotalMilliseconds;
    }


    [RelayCommand(CanExecute = nameof(CanExportTrimmedVideo))]
    private async Task ExportTrimmedVideoAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoPath))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export Trimmed Video",
            Filter = "MP4 Video Files|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            FileName = $"{Path.GetFileNameWithoutExtension(VideoPath)}_trimmed.mp4",
            InitialDirectory = Path.GetDirectoryName(VideoPath)
        };

        if (dialog.ShowDialog() != true)
            return;

        _exportCancellation?.Cancel();
        _exportCancellation?.Dispose();
        _exportCancellation = new CancellationTokenSource();

        IsExporting = true;
        ExportProgress = 0;
        ExportStatusMessage = "Preparing managed MP4 export...";
        ErrorMessage = string.Empty;

        try
        {
            IProgress<double> progress = new Progress<double>(value =>
                ExportProgress = Math.Clamp(value * 100, 0, 100));

            VideoExportResult result = await _videoExportService.ExportTrimmedVideoAsync(
                VideoPath,
                dialog.FileName,
                TrimStart / 1000.0,
                TrimEnd / 1000.0,
                progress,
                _exportCancellation.Token);

            ExportProgress = 100;

            bool adjusted =
                Math.Abs(result.RequestedStartSeconds - result.EffectiveStartSeconds) > 0.001 ||
                Math.Abs(result.RequestedEndSeconds - result.EffectiveEndSeconds) > 0.001;

            ExportStatusMessage = adjusted
                ? $"Export complete. The range was aligned to keyframes ({FormatDuration(TimeSpan.FromSeconds(result.EffectiveDurationSeconds))})."
                : "Export complete.";

            StatusMessage = $"Saved trimmed video: {Path.GetFileName(result.OutputPath)}";
        }
        catch (OperationCanceledException)
        {
            ExportStatusMessage = "Export cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
            ExportStatusMessage = "Export failed.";
        }
        finally
        {
            _exportCancellation?.Dispose();
            _exportCancellation = null;
            IsExporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelExport))]
    private void CancelExport()
    {
        _exportCancellation?.Cancel();
    }

    private async Task GenerateVideoFramesAsync(string filePath)
    {
        VideoFrames.Clear();

        if (_videoInfo == null || _videoInfo.Duration <= TimeSpan.Zero)
            return;

        const int frameCount = 20;

        for (int i = 0; i < frameCount; i++)
        {
            double fraction = (double)i / frameCount;

            TimeSpan position = TimeSpan.FromMilliseconds(
                _videoInfo.Duration.TotalMilliseconds * fraction);

            string thumbnailPath = Path.Combine(
                Path.GetTempPath(),
                $"Pixora_Frame_{Guid.NewGuid()}.png");

            try
            {
                string generatedPath =
                    await _videoService.GenerateThumbnailAsync(
                        filePath,
                        position,
                        thumbnailPath);

                if (string.IsNullOrWhiteSpace(generatedPath) ||
                    !File.Exists(generatedPath))
                {
                    continue;
                }

                BitmapImage frame;

                // Read the image into memory and close the file.
                using (FileStream stream = new FileStream(
                    generatedPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite))
                {
                    frame = new BitmapImage();

                    frame.BeginInit();
                    frame.CacheOption = BitmapCacheOption.OnLoad;
                    frame.DecodePixelWidth = 240;
                    frame.StreamSource = stream;
                    frame.EndInit();

                    frame.Freeze();
                }

                VideoFrames.Add(frame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Thumbnail {i} failed: {ex.Message}");
            }
            finally
            {
                // Give the process a moment to release any remaining file handle.
                if (File.Exists(thumbnailPath))
                {
                    try
                    {
                        File.Delete(thumbnailPath);
                    }
                    catch (IOException ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Could not delete thumbnail: {ex.Message}");
                    }
                }
            }
        }
    }
}