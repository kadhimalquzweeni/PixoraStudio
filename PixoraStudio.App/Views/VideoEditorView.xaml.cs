
using LibVLCSharp.Shared;
using PixoraStudio.App.ViewModels;
using PixoraStudio.App.Windows;
using PixoraStudio.App.Services;
using PixoraStudio.Core.Models;
using PixoraStudio.Core.Threading;
using PixoraStudio.Infrastructure.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PixoraStudio.App.Views;

public partial class VideoEditorView : UserControl
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    private VlcMediaPlayer? _mediaPlayer;
    private readonly AacAudioPlaybackService _aacAudioPlaybackService = new();
    private readonly ManagedH264VideoPlaybackService _managedH264PlaybackService = new();
    private bool _usesManagedH264Video;
    private readonly object _managedFrameSync = new();
    private BitmapSource? _pendingManagedBitmap;
    private int _pendingManagedLoadVersion;
    private bool _managedFrameDispatchPending;
    private CancellationTokenSource? _videoLoadCancellation;
    private bool _usesCustomAacAudio;
    private bool _isPreparingVideo;
    private bool _resumePlaybackAfterSeek;
    private int _seekOperationVersion;
    private int _audioSyncInProgress;
    private int _videoLoadVersion;
    private readonly AsyncOperationGate _playerLifecycleGate = new();
    private LibVLC? _libVLC;

    private VideoOverlayWindow? _overlayWindow;
    private Visibility _previousSidebarVisibility;
    private Visibility _previousHeaderVisibility;
    private GridLength _previousSidebarColumnWidth;
    private GridLength _previousHeaderRowHeight;
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(UiTickMilliseconds)
    };

    // Keeps the separate overlay aligned with the video preview.
    private readonly DispatcherTimer _overlayPositionTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };

    private bool _wasPlayingBeforeScrubbing;

    /// <summary>Open GPU (Media Foundation) reader used for scrub previews.</summary>
    private MediaFoundationFrameReader? _gpuFrameReader;
    private bool _gpuFrameReaderRequested;
    private Task? _gpuFrameReaderOpening;

    // The playhead and time label refresh at ~25 fps. Audio drift correction
    // is far more expensive, so it still runs only every fifth tick (200 ms).
    private const int GpuPreviewWidth = 960;
    private const int UiTickMilliseconds = 40;
    private const int AudioSyncEveryTicks = 5;
    private int _ticksSinceAudioSync;
    private int _scrubSeekBusy;
    private double? _pendingScrubPosition;
    private readonly PlaybackScrubState _playbackSliderScrubState = new();
    private bool _isDraggingTrimStart;
    private bool _isDraggingTrimEnd;
    private bool _isDraggingPlayhead;

    private bool _isFullScreen;

    private Window? _editorWindow;

    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private WindowState _previousWindowState;

    private Thickness _previousPreviewMargin;
    private CornerRadius _previousPreviewCornerRadius;

    public VideoEditorView()
    {
        InitializeComponent();

        Loaded += VideoEditorView_Loaded;
        Unloaded += VideoEditorView_Unloaded;

        _timer.Tick += Timer_Tick;
        _overlayPositionTimer.Tick += OverlayPositionTimer_Tick;

        DataContextChanged += VideoEditorView_DataContextChanged;
    }

    private void VideoEditorView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        _editorWindow = Window.GetWindow(this);

        if (_editorWindow != null)
        {
            _editorWindow.StateChanged -= EditorWindow_StateChanged;
            _editorWindow.StateChanged += EditorWindow_StateChanged;
        }

        if (_mediaPlayer == null)
        {
            _libVLC = App.LibVLCInstance;

            _mediaPlayer = new VlcMediaPlayer(_libVLC);
            _mediaPlayer.EndReached += MediaPlayer_EndReached;

            VideoPlayer.MediaPlayer = _mediaPlayer;
        }

        SubscribeToViewModel();

        CreateOverlayWindow();

        _timer.Start();
        RefreshOverlayWindowVisibility();

        UpdateTrimTimeline();

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(UpdateOverlayPosition));
    }

    private void VideoEditorView_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        UnsubscribeFromViewModel();
        SubscribeToViewModel();

        if (_overlayWindow != null)
            _overlayWindow.DataContext = DataContext;
    }

    private void SubscribeToViewModel()
    {
        if (DataContext is not VideoEditorViewModel vm)
            return;

        vm.VideoLoaded -= OnVideoLoaded;
        vm.VideoClosed -= OnVideoClosed;
        vm.PlayPauseRequested -= OnPlayPause;
        vm.SeekRequested -= OnSeek;
        vm.VolumeChanged -= OnVolumeChanged;
        vm.MuteChanged -= OnMuteChanged;

        vm.VideoLoaded += OnVideoLoaded;
        vm.VideoClosed += OnVideoClosed;
        vm.PlayPauseRequested += OnPlayPause;
        vm.SeekRequested += OnSeek;
        vm.VolumeChanged += OnVolumeChanged;
        vm.MuteChanged += OnMuteChanged;

        ApplyAudioOutputSettings(vm);
    }

    private void UnsubscribeFromViewModel()
    {
        if (DataContext is not VideoEditorViewModel vm)
            return;

        vm.VideoLoaded -= OnVideoLoaded;
        vm.VideoClosed -= OnVideoClosed;
        vm.PlayPauseRequested -= OnPlayPause;
        vm.SeekRequested -= OnSeek;
        vm.VolumeChanged -= OnVolumeChanged;
        vm.MuteChanged -= OnMuteChanged;
    }


    private void CreateOverlayWindow()
    {
        if (_overlayWindow != null)
            return;

        _overlayWindow = new VideoOverlayWindow
        {
            Owner = _editorWindow,
            DataContext = DataContext,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        _overlayWindow.FullscreenRequested +=
            OverlayWindow_FullscreenRequested;

        _overlayWindow.PlaybackPreviewRequested +=
            OverlayWindow_PlaybackPreviewRequested;

        _overlayWindow.PreviewMouseLeftButtonDown +=
            OverlayWindow_PreviewMouseLeftButtonDown;

        RefreshOverlayWindowVisibility();
    }

    private bool CanDisplayOverlay =>
        _editorWindow is not null &&
        VideoOverlayVisibilityPolicy.ShouldShow(
            _editorWindow.WindowState == WindowState.Minimized,
            VideoHost.IsVisible);

    private void RefreshOverlayWindowVisibility()
    {
        if (_overlayWindow is null)
            return;

        if (!CanDisplayOverlay)
        {
            _overlayWindow.HideControls();

            if (_overlayWindow.IsVisible)
                _overlayWindow.Hide();

            _overlayPositionTimer.Stop();
            return;
        }

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        _overlayPositionTimer.Start();
        UpdateOverlayPosition();
        CheckVideoHover();
    }

     private void EditorWindow_StateChanged(object? sender, EventArgs e)
    {
        RefreshOverlayWindowVisibility();
    }

    private void OverlayWindow_FullscreenRequested(
        object? sender,
        EventArgs e)
    {
        ToggleFullScreen();
    }

    private void OverlayWindow_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;

        // The overlay window owns the fullscreen slider, so its routed
        // PreviewMouseLeftButtonDown event is handled here in the editor view.
        // Capture playback state before the two-way slider binding changes.
        if (IsWithinSlider(source))
        {
            if (DataContext is VideoEditorViewModel vm &&
                vm.TotalMilliseconds > 0 &&
                _mediaPlayer is not null)
            {
                _playbackSliderScrubState.Begin(_mediaPlayer.IsPlaying);
                if (_mediaPlayer.IsPlaying)
                    PauseForScrub();
            }

            return;
        }

        if (IsPlaybackControl(source))
            return;

        if (e.ClickCount == 2)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    private void OverlayWindow_PlaybackPreviewRequested(double positionMilliseconds)
    {
        if (!_playbackSliderScrubState.IsActive ||
            DataContext is not VideoEditorViewModel vm ||
            vm.TotalMilliseconds <= 0)
            return;

        vm.CurrentTime = FormatPlaybackTime(
            TimeSpan.FromMilliseconds(positionMilliseconds));
        ScrubTo(positionMilliseconds);
    }

    private async Task OpenGpuFrameReaderAsync(string filePath, int loadVersion)
    {
        try
        {
            MediaFoundationFrameReader? reader =
                await MediaFoundationFrameReader.TryOpenAsync(filePath, GpuPreviewWidth);
            if (reader is null)
                return;

            if (loadVersion != _videoLoadVersion)
            {
                reader.Dispose();
                return;
            }

            _gpuFrameReader?.Dispose();
            _gpuFrameReader = reader;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"GPU frame reader unavailable; scrubbing uses the managed decoder: {exception.Message}");
        }
    }

    private void DisposeGpuFrameReader()
    {
        _gpuFrameReader?.Dispose();
        _gpuFrameReader = null;
    }

    /// <summary>
    /// Shows the exact frame at <paramref name="positionMilliseconds"/> decoded
    /// by the GPU. Returns false when the GPU reader is unavailable or fails,
    /// so the caller can use the managed preview instead.
    /// </summary>
    private async Task<bool> TryShowGpuFrameAsync(double positionMilliseconds)
    {
        MediaFoundationFrameReader? reader = _gpuFrameReader;
        if (reader is null)
            return false;

        try
        {
            VideoFramePixels pixels = await reader.ReadFrameAsync(
                TimeSpan.FromMilliseconds(positionMilliseconds));

            // The scrub may have ended while the GPU was decoding.
            if (!IsScrubActive)
                return true;

            BitmapSource bitmap = BitmapSource.Create(
                pixels.Width, pixels.Height, 96, 96,
                PixelFormats.Bgr32, null, pixels.Bgra, pixels.Stride);
            bitmap.Freeze();

            ScrubPreviewFrame.Source = bitmap;
            ScrubPreviewFrame.Visibility = Visibility.Visible;
            return true;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"GPU scrub frame failed; using the managed decoder instead: {exception.Message}");

            // Don't retry a reader that is failing on every request.
            if (ReferenceEquals(_gpuFrameReader, reader))
                DisposeGpuFrameReader();
            return false;
        }
    }

    private void ShowThumbnailPlaceholder(double positionMilliseconds)
    {
        if (DataContext is not VideoEditorViewModel vm ||
            vm.VideoFrames.Count == 0)
            return;

        int index = VideoScrubPreviewSelector.FindNearestThumbnailIndex(
            positionMilliseconds, vm.TotalMilliseconds, vm.VideoFrames.Count);
        if (index < 0)
            return;

        ScrubPreviewFrame.Source = vm.VideoFrames[index];
        ScrubPreviewFrame.Visibility = Visibility.Visible;
    }

    private void HideThumbnailPlaceholder()
    {
        ScrubPreviewFrame.Visibility = Visibility.Collapsed;
        ScrubPreviewFrame.Source = null;
    }

    private bool IsScrubActive =>
        _isDraggingPlayhead || _playbackSliderScrubState.IsActive;

    /// <summary>Pauses video and audio once, at the start of a scrub.</summary>
    private void PauseForScrub()
    {
        // SetPause is idempotent; Pause() toggles.
        _mediaPlayer?.SetPause(true);
        if (_usesCustomAacAudio)
            _aacAudioPlaybackService.Pause();
    }

    /// <summary>
    /// Shows a video frame near <paramref name="position"/> while the user
    /// drags. Seeks are expensive, so one runs at a time and the newest
    /// requested position replaces any that queued behind it. Audio is left
    /// alone until the scrub is committed by <see cref="OnSeek"/>.
    /// </summary>
    private async void ScrubTo(double position)
    {
        if (!IsScrubActive || _mediaPlayer is null || _isPreparingVideo)
            return;

        _pendingScrubPosition = position;

        if (!_gpuFrameReaderRequested &&
            DataContext is VideoEditorViewModel scrubVm &&
            !scrubVm.IsLoading &&
            scrubVm.VideoPath is { Length: > 0 } scrubPath)
        {
            _gpuFrameReaderRequested = true;
            _gpuFrameReaderOpening = OpenGpuFrameReaderAsync(scrubPath, _videoLoadVersion);
        }

        if (Interlocked.CompareExchange(ref _scrubSeekBusy, 1, 0) != 0)
            return;

        try
        {
            while (IsScrubActive &&
                   _pendingScrubPosition is double target &&
                   _mediaPlayer is not null)
            {
                _pendingScrubPosition = null;
                _mediaPlayer.Time = (long)target;

                // Opening the GPU reader takes a moment. Wait for it rather than
                // starting a slow software decode that would be thrown away.
                if (_gpuFrameReader is null &&
                    _gpuFrameReaderOpening is { IsCompleted: false } opening)
                {
                    ShowThumbnailPlaceholder(target);
                    await opening;
                }

                // Preferred path: the GPU decodes the exact frame at this position.
                if (await TryShowGpuFrameAsync(target))
                    continue;

                // Instant feedback: the nearest filmstrip thumbnail is already in
                // memory. The decoded keyframe below replaces it when ready.
                ShowThumbnailPlaceholder(target);

                // LibVLC owns the visible video surface. A paused player does
                // not draw the frame at a new Time until it is asked to step,
                // so refresh it for both the managed and LibVLC-only paths.
                _mediaPlayer.NextFrame();

                // Also publish the managed picture: one cached keyframe instead
                // of decoding forward to the exact frame on every move.
                // OnSeek lands on the exact frame when the scrub ends.
                if (_usesManagedH264Video)
                {
                    bool shown = await _managedH264PlaybackService
                        .ShowKeyframePreviewAsync(target / 1000d);

                    // Fall back to the exact seek that the release uses.
                    if (!shown && IsScrubActive)
                        await _managedH264PlaybackService.SeekAsync(target / 1000d);

                    // The real picture is up; drop the placeholder unless the
                    // pointer has already moved on to a newer position.
                    if (_pendingScrubPosition is null)
                        HideThumbnailPlaceholder();
                }
                else
                {
                    // Give the asynchronous LibVLC seek a moment, then step again
                    // so the frame at the new position is the one drawn.
                    await Task.Delay(40);
                    if (IsScrubActive && _mediaPlayer is not null)
                        _mediaPlayer.NextFrame();
                }
            }
        }
        catch (Exception exception)
        {
            // The committing seek on release reports or recovers from real failures.
            System.Diagnostics.Debug.WriteLine($"Scrub seek failed: {exception.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _scrubSeekBusy, 0);
        }
    }

    private static bool IsWithinSlider(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Slider)
                return true;

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private bool IsPlaybackControl(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button || element is Slider)
                return true;

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void OverlayPositionTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (!CanDisplayOverlay)
        {
            RefreshOverlayWindowVisibility();
            return;
        }

        // Do not move the layered overlay while WPF owns the mouse for a
        // slider drag. Repositioning a transparent top-level window during
        // capture can interrupt the drag and make the timeline intermittent.
        if (_overlayWindow?.IsMouseCaptureWithin == true ||
            Mouse.Captured is not null)
        {
            CheckVideoHover();
            return;
        }

        UpdateOverlayPosition();
        CheckVideoHover();
    }

    private void UpdateOverlayPosition()
    {
        if (_overlayWindow == null ||
            !CanDisplayOverlay ||
            !VideoHost.IsVisible ||
            VideoHost.ActualWidth <= 0 ||
            VideoHost.ActualHeight <= 0)
        {
            return;
        }

        var presentationSource = PresentationSource.FromVisual(VideoHost);

        if (presentationSource?.CompositionTarget == null)
            return;

        // PointToScreen returns screen coordinates.
        Point topLeft = VideoHost.PointToScreen(new Point(0, 0));

        Point bottomRight = VideoHost.PointToScreen(
            new Point(VideoHost.ActualWidth, VideoHost.ActualHeight));

        var transform = presentationSource.CompositionTarget.TransformFromDevice;

        topLeft = transform.Transform(topLeft);
        bottomRight = transform.Transform(bottomRight);

        // Avoid resetting native window bounds every timer tick. Reapplying
        // unchanged bounds can interrupt mouse capture on a layered WPF window.
        SetOverlayBounds(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
    }

    private void MediaPlayer_EndReached(object? sender, EventArgs e)
    {
        // LibVLC raises EndReached on its playback thread. Marshal UI state
        // and the custom output shutdown back to the WPF dispatcher.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
        {
            if (_isPreparingVideo ||
                !_usesCustomAacAudio ||
                _mediaPlayer?.IsPlaying == true)
            {
                return;
            }

            _aacAudioPlaybackService.Stop();

            if (DataContext is VideoEditorViewModel vm)
            {
                vm.IsPlaying = false;

                if (_mediaPlayer is { Length: > 0 } player)
                {
                    vm.PlaybackPosition = player.Length;
                    vm.CurrentTime = FormatPlaybackTime(
                        TimeSpan.FromMilliseconds(player.Length));
                }
            }
        }));
    }

    private static void LogVideoLoad(int loadVersion, string message)
    {
        Debug.WriteLine($"[{DateTimeOffset.Now:O}] [VideoLoad:{loadVersion}] [Thread:{Environment.CurrentManagedThreadId}] {message}");
    }

    private static void LogVideoLoadStep(int loadVersion, string operation, Action action)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        LogVideoLoad(loadVersion, $"{operation} started");
        try
        {
            action();
            LogVideoLoad(loadVersion, $"{operation} completed in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception exception)
        {
            LogVideoLoad(loadVersion, $"{operation} FAILED after {stopwatch.ElapsedMilliseconds} ms: {exception}");
            throw;
        }
    }

    private async void OnVideoLoaded(string filePath)
    {
        if (_mediaPlayer == null || _libVLC == null)
            return;

        int loadVersion = ++_videoLoadVersion;
        Stopwatch totalLoad = Stopwatch.StartNew();
        LogVideoLoad(loadVersion, $"replacement requested: '{filePath}'");
        Interlocked.Increment(ref _seekOperationVersion);
        _resumePlaybackAfterSeek = false;
        _isPreparingVideo = true;

        LogVideoLoadStep(loadVersion, "DisposeGpuFrameReader", DisposeGpuFrameReader);
        _gpuFrameReaderRequested = false;
        _gpuFrameReaderOpening = null;

        LogVideoLoadStep(loadVersion, "Cancel/dispose previous load cancellation", () =>
        {
            _videoLoadCancellation?.Cancel();
            _videoLoadCancellation?.Dispose();
        });
        _videoLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _videoLoadCancellation.Token;

        VlcMediaPlayer player = _mediaPlayer;
        LibVLC libVlc = _libVLC;
        bool mediaStarted = false;

        try
        {
            // All native player lifecycle changes share one asynchronous gate.
            // A newer load waits for the previous Stop() to finish, and close or
            // unload cannot dispose the player while a load is using it.
            Stopwatch gateWait = Stopwatch.StartNew();
            LogVideoLoad(loadVersion, "waiting for player lifecycle gate");
            await _playerLifecycleGate.ExecuteAsync(async () =>
            {
                LogVideoLoad(loadVersion, $"acquired player lifecycle gate after {gateWait.ElapsedMilliseconds} ms");
                Stopwatch vlcStop = Stopwatch.StartNew();
                LogVideoLoad(loadVersion, "scheduling LibVLC player.Stop() on worker task");
                await Task.Run(() =>
                {
                    Stopwatch workerStop = Stopwatch.StartNew();
                    LogVideoLoad(loadVersion, "LibVLC player.Stop() ENTERED on worker thread");
                    try
                    {
                        player.Stop();
                        LogVideoLoad(loadVersion, $"LibVLC player.Stop() RETURNED on worker thread in {workerStop.ElapsedMilliseconds} ms");
                    }
                    catch (Exception exception)
                    {
                        LogVideoLoad(loadVersion, $"LibVLC player.Stop() THREW on worker thread after {workerStop.ElapsedMilliseconds} ms: {exception}");
                        throw;
                    }
                });
                LogVideoLoad(loadVersion, $"LibVLC player.Stop() task completed; total scheduling/wait time {vlcStop.ElapsedMilliseconds} ms");

                if (loadVersion != _videoLoadVersion ||
                    cancellationToken.IsCancellationRequested ||
                    !ReferenceEquals(_mediaPlayer, player))
                {
                    return;
                }

                player.Mute = false;
                _usesManagedH264Video = false;
                ManagedVideoFrame.Source = null;
                ManagedVideoFrame.Visibility = Visibility.Collapsed;
                ScrubPreviewFrame.Source = null;
                ScrubPreviewFrame.Visibility = Visibility.Collapsed;

                _aacAudioPlaybackService.Unload(loadVersion);
                _usesCustomAacAudio = false;

                if (loadVersion != _videoLoadVersion ||
                    cancellationToken.IsCancellationRequested ||
                    !ReferenceEquals(_mediaPlayer, player))
                {
                    return;
                }

                Stopwatch createMedia = Stopwatch.StartNew();
                LogVideoLoad(loadVersion, "LibVLC Media construction started");
                using var media = new Media(libVlc, filePath, FromType.FromPath);
                LogVideoLoad(loadVersion, $"LibVLC Media construction completed in {createMedia.ElapsedMilliseconds} ms");

                if (DataContext is VideoEditorViewModel audioVm)
                    LogVideoLoadStep(loadVersion, "ApplyAudioOutputSettings", () => ApplyAudioOutputSettings(audioVm));

                Stopwatch vlcPlay = Stopwatch.StartNew();
                LogVideoLoad(loadVersion, "LibVLC player.Play() started");
                player.Play(media);
                LogVideoLoad(loadVersion, $"LibVLC player.Play() returned in {vlcPlay.ElapsedMilliseconds} ms");
                _isPreparingVideo = false;
                mediaStarted = true;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            LogVideoLoad(loadVersion, $"load cancelled after {totalLoad.ElapsedMilliseconds} ms during player setup");
            return;
        }
        catch (Exception exception)
        {
            LogVideoLoad(loadVersion, $"player setup FAILED after {totalLoad.ElapsedMilliseconds} ms: {exception}");
            if (loadVersion == _videoLoadVersion)
            {
                _isPreparingVideo = false;
                if (DataContext is VideoEditorViewModel vm)
                    vm.ErrorMessage = $"Unable to open video: {exception.Message}";
            }

            System.Diagnostics.Debug.WriteLine(
                $"Video replacement failed: {exception}");
            return;
        }

        if (!mediaStarted ||
            loadVersion != _videoLoadVersion ||
            cancellationToken.IsCancellationRequested)
        {
            LogVideoLoad(loadVersion, $"managed H.264 startup skipped; mediaStarted={mediaStarted}, currentLoad={_videoLoadVersion}, cancelled={cancellationToken.IsCancellationRequested}");
            return;
        }

        try
        {
            Stopwatch managedGateWait = Stopwatch.StartNew();
            LogVideoLoad(loadVersion, "waiting for lifecycle gate before managed H.264 startup");
            await _playerLifecycleGate.ExecuteAsync(async () =>
            {
                LogVideoLoad(loadVersion, $"acquired lifecycle gate for managed H.264 after {managedGateWait.ElapsedMilliseconds} ms");
                if (loadVersion != _videoLoadVersion ||
                    cancellationToken.IsCancellationRequested ||
                    !ReferenceEquals(_mediaPlayer, player))
                {
                    return;
                }

                Stopwatch managedStart = Stopwatch.StartNew();
                LogVideoLoad(loadVersion, "ManagedH264VideoPlaybackService.TryStartAsync started");
                _usesManagedH264Video = await _managedH264PlaybackService.TryStartAsync(
                    filePath,
                    () => player.Time / 1000d,
                    bitmap => QueueManagedFrame(bitmap, loadVersion),
                    exception => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (loadVersion != _videoLoadVersion)
                            return;

                        _usesManagedH264Video = false;
                        ManagedVideoFrame.Source = null;
                        ManagedVideoFrame.Visibility = Visibility.Collapsed;
                        System.Diagnostics.Debug.WriteLine(
                            $"Managed H.264 preview stopped; LibVLC fallback remains active: {exception.Message}");
                    })),
                    cancellationToken);
                LogVideoLoad(loadVersion, $"ManagedH264VideoPlaybackService.TryStartAsync completed in {managedStart.ElapsedMilliseconds} ms; active={_usesManagedH264Video}");

                if (loadVersion == _videoLoadVersion && !_usesManagedH264Video)
                    ManagedVideoFrame.Visibility = Visibility.Collapsed;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            LogVideoLoad(loadVersion, $"managed H.264 startup cancelled after {totalLoad.ElapsedMilliseconds} ms");
        }
        catch (Exception exception)
        {
            LogVideoLoad(loadVersion, $"managed H.264 startup FAILED after {totalLoad.ElapsedMilliseconds} ms: {exception}");
            if (loadVersion == _videoLoadVersion)
            {
                _usesManagedH264Video = false;
                ManagedVideoFrame.Visibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine(
                    $"Managed H.264 playback could not start; LibVLC fallback remains active: {exception}");
            }
        }

        LogVideoLoad(loadVersion, $"replacement handler finished after {totalLoad.ElapsedMilliseconds} ms");
    }

    private async void OnVideoClosed()
    {
        int closeVersion = ++_videoLoadVersion;
        Interlocked.Increment(ref _seekOperationVersion);
        _resumePlaybackAfterSeek = false;
        _isPreparingVideo = false;
        _videoLoadCancellation?.Cancel();
        _videoLoadCancellation?.Dispose();
        _videoLoadCancellation = null;
        _aacAudioPlaybackService.Unload();
        DisposeGpuFrameReader();
        _usesManagedH264Video = false;
        ManagedVideoFrame.Source = null;
        ManagedVideoFrame.Visibility = Visibility.Collapsed;
        ScrubPreviewFrame.Source = null;
        ScrubPreviewFrame.Visibility = Visibility.Collapsed;
        _usesCustomAacAudio = false;

        VlcMediaPlayer? player = _mediaPlayer;
        if (player is null)
            return;

        try
        {
            await _playerLifecycleGate.ExecuteAsync(async () =>
            {
                // If a new file was selected while this close was waiting, its
                // load owns the player now; do not stop that newer playback.
                if (closeVersion != _videoLoadVersion ||
                    !ReferenceEquals(_mediaPlayer, player))
                {
                    return;
                }

                await _managedH264PlaybackService.StopAsync();

                if (closeVersion != _videoLoadVersion ||
                    !ReferenceEquals(_mediaPlayer, player))
                {
                    return;
                }

                await Task.Run(player.Stop);
                if (closeVersion == _videoLoadVersion &&
                    ReferenceEquals(_mediaPlayer, player))
                {
                    player.Mute = false;
                }
            });
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Stopping the closed video failed: {exception}");
        }
    }

    private async void OnPlayPause()
    {
        if (_isPreparingVideo || _mediaPlayer == null)
            return;

        if (_mediaPlayer.IsPlaying ||
            (_usesCustomAacAudio &&
             _aacAudioPlaybackService.IsSeeking &&
             _resumePlaybackAfterSeek))
        {
            _mediaPlayer.Pause();
            if (_usesCustomAacAudio)
                _aacAudioPlaybackService.Pause();

            _resumePlaybackAfterSeek = false;
        }
        else
        {
            if (_usesCustomAacAudio)
            {
                try
                {
                    await _aacAudioPlaybackService.PlayAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Custom AAC playback restart failed: {ex.Message}");
                }
            }

            _mediaPlayer.Play();
        }
    }

    private async void OnSeek(double position)
    {
        if (_isPreparingVideo || _mediaPlayer == null)
            return;

        int seekVersion = Interlocked.Increment(ref _seekOperationVersion);
        _pendingScrubPosition = null;

        // Use the state captured on pointer-down for fullscreen scrubbing.
        // The decoder may temporarily report paused while a seek is committed.
        bool resumePlayback = _playbackSliderScrubState.IsActive
            ? _playbackSliderScrubState.Complete()
            : _mediaPlayer.IsPlaying ||
              _wasPlayingBeforeScrubbing ||
              _resumePlaybackAfterSeek;

        _resumePlaybackAfterSeek = resumePlayback;

        double targetPosition = DataContext is VideoEditorViewModel seekVm &&
            seekVm.TotalMilliseconds > 0
                ? VideoTimelineMath.ClampPosition(position, seekVm.TotalMilliseconds)
                : Math.Max(0, position);

        if (DataContext is VideoEditorViewModel currentVm)
        {
            currentVm.PlaybackPosition = targetPosition;
            currentVm.CurrentTime = FormatPlaybackTime(
                TimeSpan.FromMilliseconds(targetPosition));
        }

        // SetPause is idempotent. Pause() toggles, so calling it on a player
        // the scrub had already paused would restart it mid-seek.
        if (resumePlayback)
            _mediaPlayer.SetPause(true);

        _mediaPlayer.Time = (long)targetPosition;

        if (_usesManagedH264Video)
        {
            try
            {
                await _managedH264PlaybackService.SeekAsync(targetPosition / 1000d);
            }
            catch (Exception exception)
            {
                _usesManagedH264Video = false;
                ManagedVideoFrame.Source = null;
                ManagedVideoFrame.Visibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine(
                    $"Managed H.264 seek failed; LibVLC fallback remains active: {exception.Message}");
            }
        }

        // The real player/managed decoder has now received the committed seek.
        // Remove the temporary filmstrip image so the actual video frame is visible.
        ScrubPreviewFrame.Visibility = Visibility.Collapsed;
        ScrubPreviewFrame.Source = null;

        if (!_usesCustomAacAudio)
        {
            if (resumePlayback && seekVersion == Volatile.Read(ref _seekOperationVersion))
                _mediaPlayer.Play();

            _resumePlaybackAfterSeek = false;
            return;
        }

        try
        {
            await _aacAudioPlaybackService.SeekAsync(
                TimeSpan.FromMilliseconds(targetPosition),
                resumePlayback);

            if (resumePlayback &&
                _resumePlaybackAfterSeek &&
                seekVersion == Volatile.Read(ref _seekOperationVersion) &&
                _mediaPlayer is not null)
            {
                _mediaPlayer.Play();
            }

            if (seekVersion == Volatile.Read(ref _seekOperationVersion))
                _resumePlaybackAfterSeek = false;
        }
        catch (OperationCanceledException)
        {
            // A newer seek or video close superseded this request.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Custom AAC seek failed: {ex.Message}");

            if (resumePlayback &&
                _resumePlaybackAfterSeek &&
                seekVersion == Volatile.Read(ref _seekOperationVersion) &&
                _mediaPlayer is not null)
            {
                _mediaPlayer.Play();
            }

            if (seekVersion == Volatile.Read(ref _seekOperationVersion))
                _resumePlaybackAfterSeek = false;
        }
    }

    /// <summary>
    /// Coalesces preview updates so a busy WPF dispatcher renders the newest
    /// due frame instead of accumulating a queue of already-stale bitmaps.
    /// </summary>
    private void QueueManagedFrame(BitmapSource bitmap, int loadVersion)
    {
        if (loadVersion != Volatile.Read(ref _videoLoadVersion))
            return;

        bool scheduleDispatch;
        lock (_managedFrameSync)
        {
            if (loadVersion != _videoLoadVersion)
                return;

            _pendingManagedBitmap = bitmap;
            _pendingManagedLoadVersion = loadVersion;
            scheduleDispatch = !_managedFrameDispatchPending;
            _managedFrameDispatchPending = true;
        }

        if (!scheduleDispatch)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            BitmapSource? nextBitmap;
            int nextLoadVersion;
            lock (_managedFrameSync)
            {
                nextBitmap = _pendingManagedBitmap;
                nextLoadVersion = _pendingManagedLoadVersion;
                _pendingManagedBitmap = null;
                _managedFrameDispatchPending = false;
            }

            if (nextLoadVersion != _videoLoadVersion || nextBitmap is null)
                return;

            ManagedVideoFrame.Source = nextBitmap;
            ManagedVideoFrame.Visibility = Visibility.Visible;
        }));
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is VideoEditorViewModel vm)
            vm.IsMuted = !vm.IsMuted;
    }

    private void ApplyAudioOutputSettings(VideoEditorViewModel vm)
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            vm.Volume,
            vm.IsMuted,
            _usesCustomAacAudio);

        // Apply the settings to both outputs. LibVLC must remain silent while
        // custom AAC is active, and neither output should retain an old level.
        _aacAudioPlaybackService.Volume = settings.PcmVolume;
        _aacAudioPlaybackService.IsMuted = settings.IsMuted;

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Volume = settings.LibVlcVolume;
            _mediaPlayer.Mute = settings.LibVlcMuted;
        }
    }

    private void OnMuteChanged(bool isMuted)
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            DataContext is VideoEditorViewModel vm ? vm.Volume : 100d,
            isMuted,
            _usesCustomAacAudio);

        _aacAudioPlaybackService.IsMuted = settings.IsMuted;

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Volume = settings.LibVlcVolume;
            _mediaPlayer.Mute = settings.LibVlcMuted;
        }
    }

    private void OnVolumeChanged(double value)
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            value,
            DataContext is VideoEditorViewModel vm && vm.IsMuted,
            _usesCustomAacAudio);

        // Keep both engines at the same level, even though only one should
        // currently be audible. A zero slider value is a true zero on both.
        _aacAudioPlaybackService.Volume = settings.PcmVolume;
        _aacAudioPlaybackService.IsMuted = settings.IsMuted;

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Volume = settings.LibVlcVolume;
            _mediaPlayer.Mute = settings.LibVlcMuted;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_mediaPlayer == null ||
            DataContext is not VideoEditorViewModel vm)
        {
            return;
        }

        vm.IsPlaying = _mediaPlayer.IsPlaying;

        // LibVLC may report Length == 0 during opening or for some
        // containers. Its Time remains the playback clock; don't gate the
        // timeline on Length becoming available.
        // PlaybackPosition is two-way bound to the fullscreen Slider.
        // Do not overwrite the thumb while the user is dragging it.
        if (!_isDraggingPlayhead && !_playbackSliderScrubState.IsActive)
        {
            double playbackMilliseconds = Math.Max(0, _mediaPlayer.Time);

            if (vm.TotalMilliseconds > 0)
            {
                playbackMilliseconds = VideoTimelineMath.ClampPosition(
                    playbackMilliseconds,
                    vm.TotalMilliseconds);
            }

            vm.PlaybackPosition = playbackMilliseconds;
            vm.CurrentTime = FormatPlaybackTime(
                TimeSpan.FromMilliseconds(playbackMilliseconds));
        }

        if (++_ticksSinceAudioSync >= AudioSyncEveryTicks)
        {
            _ticksSinceAudioSync = 0;

            if (_usesCustomAacAudio &&
                _mediaPlayer.IsPlaying &&
                !_aacAudioPlaybackService.IsSeeking)
            {
                _ = SynchroniseAudioAsync(
                    TimeSpan.FromMilliseconds(_mediaPlayer.Time));
            }
        }

        UpdateTrimTimeline();
    }

    private async Task SynchroniseAudioAsync(TimeSpan videoPosition)
    {
        // Timer ticks arrive every 200 ms. Never overlap expensive AAC
        // decoder rebuilds; the active operation will compare against the
        // latest video clock on the next tick if another correction is needed.
        if (Interlocked.CompareExchange(ref _audioSyncInProgress, 1, 0) != 0)
            return;

        try
        {
            // LibVLC is the master clock. Correct only the independently
            // clocked audio output; pausing/restarting video for every drift
            // check introduces visible stutter even when no correction is due.
            await _aacAudioPlaybackService.SynchroniseAsync(videoPosition);
        }
        catch (OperationCanceledException)
        {
            // A manual seek or unload superseded clock correction.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"AAC audio synchronisation failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _audioSyncInProgress, 0);
        }
    }

    private static string FormatPlaybackTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss")
            : time.ToString(@"mm\:ss");
    }

    private void TrimTimeline_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is not VideoEditorViewModel vm ||
            vm.TotalMilliseconds <= 0)
        {
            return;
        }

        double x = e.GetPosition(TrimTimelineCanvas).X;
        double width = TrimTimelineCanvas.ActualWidth;

        if (width <= 0)
            return;

        double startX =
            vm.TrimStart / vm.TotalMilliseconds * width;

        double endX =
            vm.TrimEnd / vm.TotalMilliseconds * width;

        double startDistance = Math.Abs(x - startX);
        double endDistance = Math.Abs(x - endX);

        _isDraggingTrimStart = false;
        _isDraggingTrimEnd = false;
        _isDraggingPlayhead = false;

        if (startDistance <= 16 || endDistance <= 16)
        {
            if (startDistance <= endDistance)
                _isDraggingTrimStart = true;
            else
                _isDraggingTrimEnd = true;
        }
        else
        {
            _isDraggingPlayhead = true;

            _wasPlayingBeforeScrubbing =
                _mediaPlayer?.IsPlaying == true;

            if (_wasPlayingBeforeScrubbing)
                PauseForScrub();

            UpdatePlayheadPosition(x, vm, width);
        }

        TrimTimelineCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void TrimTimeline_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (DataContext is not VideoEditorViewModel vm ||
            vm.TotalMilliseconds <= 0)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        double width = TrimTimelineCanvas.ActualWidth;

        if (width <= 0)
            return;

        double x = e.GetPosition(TrimTimelineCanvas).X;

        double position = VideoTimelineMath.GetPosition(
            x, width, vm.TotalMilliseconds);

        if (_isDraggingTrimStart)
        {
            vm.TrimStart = Math.Min(
                position,
                Math.Max(0, vm.TrimEnd - 1));
        }
        else if (_isDraggingTrimEnd)
        {
            vm.TrimEnd = Math.Max(
                position,
                Math.Min(vm.TotalMilliseconds, vm.TrimStart + 1));
        }
        else if (_isDraggingPlayhead)
        {
            UpdatePlayheadPosition(x, vm, width);
        }

        UpdateTrimTimeline();
    }

    private void TrimTimeline_MouseUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isDraggingPlayhead &&
            DataContext is VideoEditorViewModel vm)
        {
            vm.SeekCommand.Execute(vm.PlaybackPosition);
        }

        _wasPlayingBeforeScrubbing = false;

        _isDraggingTrimStart = false;
        _isDraggingTrimEnd = false;
        _isDraggingPlayhead = false;

        TrimTimelineCanvas.ReleaseMouseCapture();

        UpdateTrimTimeline();

        e.Handled = true;
    }

    private void TrimTimeline_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdateTrimTimeline();
    }

    private void UpdatePlayheadPosition(
        double x,
        VideoEditorViewModel vm,
        double width)
    {
        double position = VideoTimelineMath.GetPosition(
            x, width, vm.TotalMilliseconds);

        vm.PlaybackPosition = position;

        vm.CurrentTime = FormatPlaybackTime(
            TimeSpan.FromMilliseconds(position));

        ScrubTo(position);
    }

    private void UpdateTrimTimeline()
    {
        if (DataContext is not VideoEditorViewModel vm)
            return;

        double width = TrimTimelineCanvas.ActualWidth;

        if (width <= 0 || vm.TotalMilliseconds <= 0)
            return;

        Filmstrip.Width = width;

        double startX = VideoTimelineMath.GetCoordinate(
            vm.TrimStart, width, vm.TotalMilliseconds);

        double endX = VideoTimelineMath.GetCoordinate(
            vm.TrimEnd, width, vm.TotalMilliseconds);

        double playheadX = VideoTimelineMath.GetCoordinate(
            vm.PlaybackPosition, width, vm.TotalMilliseconds);

        // Selected trim section.
        Canvas.SetLeft(SelectionRange, startX);
        SelectionRange.Width = Math.Max(0, endX - startX);

        // Trim start handle.
        Canvas.SetLeft(
            LeftTrimHandle,
            Math.Clamp(startX - 6, 0, Math.Max(0, width - 12)));

        // Trim end handle.
        Canvas.SetLeft(
            RightTrimHandle,
            Math.Clamp(endX - 6, 0, Math.Max(0, width - 12)));

        // Playback playhead.
        Canvas.SetLeft(
            Playhead,
            Math.Clamp(playheadX - 1.5, 0, Math.Max(0, width - 3)));

        // Update displayed times.
        PlayheadTimeLabel.Text = vm.CurrentTime;
        TrimStartTimeLabel.Text = vm.TrimStartTime;
        TrimEndTimeLabel.Text = vm.TrimEndTime;

        MeasureTimeLabel(PlayheadTimeLabel);
        MeasureTimeLabel(TrimStartTimeLabel);
        MeasureTimeLabel(TrimEndTimeLabel);

        double startWidth = TrimStartTimeLabel.DesiredSize.Width;
        double endWidth = TrimEndTimeLabel.DesiredSize.Width;
        double playheadWidth = PlayheadTimeLabel.DesiredSize.Width;

        bool playheadNearStart =
            Math.Abs(playheadX - startX) <
            (playheadWidth + startWidth) / 2 + 8;

        bool playheadNearEnd =
            Math.Abs(playheadX - endX) <
            (playheadWidth + endWidth) / 2 + 8;

        bool trimLabelsNear =
            Math.Abs(endX - startX) <
            (startWidth + endWidth) / 2 + 8;

        PositionTimeLabel(
            TrimStartTimeLabel,
            startX,
            width,
            0);

        PositionTimeLabel(
            TrimEndTimeLabel,
            endX,
            width,
            trimLabelsNear ? 48 : 0);

        double playheadTop =
            playheadNearStart || playheadNearEnd ? 48 : 0;

        if (playheadTop == 48 && trimLabelsNear)
            playheadTop = 24;

        PositionTimeLabel(
            PlayheadTimeLabel,
            playheadX,
            width,
            playheadTop);
    }

    private void MeasureTimeLabel(TextBlock label)
    {
        label.Measure(new Size(
            double.PositiveInfinity,
            double.PositiveInfinity));
    }

    private void PositionTimeLabel(
        TextBlock label,
        double markerX,
        double timelineWidth,
        double top)
    {
        double labelWidth = label.DesiredSize.Width;

        double left = markerX - labelWidth / 2;

        left = Math.Clamp(
            left,
            0,
            Math.Max(0, timelineWidth - labelWidth));

        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
    }

    // Apply the final track-click value after WPF moves the thumb to the
    // clicked point. This makes a normal click work without relying on capture.
    private void VolumeSlider_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is not VideoEditorViewModel vm)
            return;

        double volume = VideoTimelineMath.ClampSliderValue(
            VolumeSlider.Value,
            VolumeSlider.Minimum,
            VolumeSlider.Maximum);

        vm.ChangeVolumeCommand.Execute(volume);
    }

    private void VolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is VideoEditorViewModel vm)
            vm.ChangeVolumeCommand.Execute(e.NewValue);
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
            ExitFullScreen();
        else
            EnterFullScreen();
    }

    private void EnterFullScreen()
    {
        if (_isFullScreen)
            return;

        _editorWindow = Window.GetWindow(this);

        if (_editorWindow == null)
            return;

        _previousWindowStyle = _editorWindow.WindowStyle;
        _previousResizeMode = _editorWindow.ResizeMode;
        _previousWindowState = _editorWindow.WindowState;

        _previousPreviewMargin = VideoPreviewBorder.Margin;
        _previousPreviewCornerRadius = VideoPreviewBorder.CornerRadius;

        // Save the main application layout visibility.
        if (_editorWindow is MainWindow mainWindow)
        {
            // Save the original layout dimensions.
            _previousSidebarVisibility = mainWindow.SidebarBorder.Visibility;
            _previousHeaderVisibility = mainWindow.TopHeaderBorder.Visibility;

            _previousSidebarColumnWidth = mainWindow.SidebarColumn.Width;
            _previousHeaderRowHeight = mainWindow.HeaderRow.Height;

            // Hide sidebar and header.
            mainWindow.SidebarBorder.Visibility = Visibility.Collapsed;
            mainWindow.TopHeaderBorder.Visibility = Visibility.Collapsed;

            // Remove the space reserved for them.
            mainWindow.SidebarColumn.Width = new GridLength(0);
            mainWindow.HeaderRow.Height = new GridLength(0);
        }

        _isFullScreen = true;

        _overlayWindow?.SetFullscreenButton(true);

        ToolbarBorder.Visibility = Visibility.Collapsed;
        BottomEditingPanel.Visibility = Visibility.Collapsed;
        PlaybackControlsBorder.Visibility = Visibility.Collapsed;

        VideoPreviewBorder.Margin = new Thickness(0);
        VideoPreviewBorder.CornerRadius = new CornerRadius(0);

        _editorWindow.WindowState = WindowState.Normal;
        _editorWindow.WindowStyle = WindowStyle.None;
        _editorWindow.ResizeMode = ResizeMode.NoResize;
        _editorWindow.WindowState = WindowState.Maximized;

        _editorWindow.PreviewKeyDown += FullScreenWindow_PreviewKeyDown;

        _overlayPositionTimer.Start();

        // Wait for WPF to finish resizing the video preview.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                UpdateOverlayPosition();

                RefreshOverlayWindowVisibility();

                if (CanDisplayOverlay)
                    _overlayWindow?.ShowControls();
            }));
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen || _editorWindow == null)
            return;

        _isFullScreen = false;

        _editorWindow.PreviewKeyDown -=
            FullScreenWindow_PreviewKeyDown;

        // Restore the editor controls.
        ToolbarBorder.Visibility = Visibility.Visible;
        BottomEditingPanel.Visibility = Visibility.Visible;
        PlaybackControlsBorder.Visibility = Visibility.Visible;

        // Restore the preview's original appearance.
        VideoPreviewBorder.Margin = _previousPreviewMargin;
        VideoPreviewBorder.CornerRadius = _previousPreviewCornerRadius;

        // Restore the original window settings.
        _editorWindow.WindowState = WindowState.Normal;
        _editorWindow.WindowStyle = _previousWindowStyle;
        _editorWindow.ResizeMode = _previousResizeMode;
        _editorWindow.WindowState = _previousWindowState;

        // Restore the main application sidebar and header.
        if (_editorWindow is MainWindow mainWindow)
        {
            // Restore sidebar and header.
            mainWindow.SidebarBorder.Visibility = _previousSidebarVisibility;
            mainWindow.TopHeaderBorder.Visibility = _previousHeaderVisibility;

            // Restore original layout dimensions.
            mainWindow.SidebarColumn.Width = _previousSidebarColumnWidth;
            mainWindow.HeaderRow.Height = _previousHeaderRowHeight;
        }
        _overlayWindow?.SetFullscreenButton(false);

        _overlayPositionTimer.Start();

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                UpdateOverlayPosition();

                if (_overlayWindow != null)
                {
                    RefreshOverlayWindowVisibility();

                    if (CanDisplayOverlay && _overlayWindow.IsMouseOver)
                        _overlayWindow.ShowControls();
                    else
                        _overlayWindow.HideControls();
                }
            }));

    }

    private void FullScreenWindow_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ExitFullScreen();
            e.Handled = true;
        }
    }

    private async void VideoEditorView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        Interlocked.Increment(ref _seekOperationVersion);
        _resumePlaybackAfterSeek = false;
        _timer.Stop();
        _overlayPositionTimer.Stop();

        _videoLoadVersion++;
        _isPreparingVideo = false;
        _videoLoadCancellation?.Cancel();
        _videoLoadCancellation?.Dispose();
        _videoLoadCancellation = null;

        _aacAudioPlaybackService.Unload();
        DisposeGpuFrameReader();
        _usesManagedH264Video = false;
        ManagedVideoFrame.Source = null;
        ManagedVideoFrame.Visibility = Visibility.Collapsed;
        ScrubPreviewFrame.Source = null;
        ScrubPreviewFrame.Visibility = Visibility.Collapsed;
        _usesCustomAacAudio = false;

        if (_isFullScreen)
            ExitFullScreen();

        UnsubscribeFromViewModel();

        if (_overlayWindow != null)
        {
            _overlayWindow.FullscreenRequested -=
                OverlayWindow_FullscreenRequested;

            _overlayWindow.PlaybackPreviewRequested -=
                OverlayWindow_PlaybackPreviewRequested;

            _overlayWindow.PreviewMouseLeftButtonDown -=
                OverlayWindow_PreviewMouseLeftButtonDown;

            _overlayWindow.Close();
            _overlayWindow = null;
        }

        if (_editorWindow != null)
        {
            _editorWindow.PreviewKeyDown -=
                FullScreenWindow_PreviewKeyDown;
            _editorWindow.StateChanged -= EditorWindow_StateChanged;
        }

        VlcMediaPlayer? playerToDispose = _mediaPlayer;
        if (playerToDispose is not null)
            playerToDispose.EndReached -= MediaPlayer_EndReached;

        // Detach immediately so any suspended load continuation knows the view
        // is shutting down. Actual native disposal waits behind the lifecycle gate.
        _mediaPlayer = null;
        VideoPlayer.MediaPlayer = null;

        try
        {
            await _playerLifecycleGate.ExecuteAsync(async () =>
            {
                await _managedH264PlaybackService.StopAsync();

                if (playerToDispose is not null)
                {
                    await Task.Run(() =>
                    {
                        playerToDispose.Stop();
                        playerToDispose.Dispose();
                    });
                }
            });
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Disposing the LibVLC player during unload failed: {exception}");
        }
    }

    private void SetOverlayBounds(
        double left,
        double top,
        double width,
        double height)
    {
        const double tolerance = 0.5;

        if (Math.Abs(_overlayWindow!.Left - left) > tolerance)
            _overlayWindow.Left = left;

        if (Math.Abs(_overlayWindow.Top - top) > tolerance)
            _overlayWindow.Top = top;

        if (Math.Abs(_overlayWindow.Width - width) > tolerance)
            _overlayWindow.Width = width;

        if (Math.Abs(_overlayWindow.Height - height) > tolerance)
            _overlayWindow.Height = height;
    }

    private void CheckVideoHover()
    {
        if (_overlayWindow == null ||
            !CanDisplayOverlay ||
            !VideoHost.IsVisible)
        {
            return;
        }

        // Keep controls alive while the pointer is interacting with the
        // overlay. The timer must not hide a button during its click/capture.
        if (VideoOverlayVisibilityPolicy.ShouldKeepControlsVisible(
            _overlayWindow.IsMouseOver,
            _overlayWindow.IsMouseCaptureWithin,
            Mouse.Captured is not null))
        {
            _overlayWindow.ShowControls();
            return;
        }

        if (!GetCursorPos(out NativePoint cursor))
            return;

        Point topLeft = VideoHost.PointToScreen(new Point(0, 0));

        Point bottomRight = VideoHost.PointToScreen(
            new Point(
                VideoHost.ActualWidth,
                VideoHost.ActualHeight));

        bool isOverVideo =
            cursor.X >= topLeft.X &&
            cursor.Y >= topLeft.Y &&
            cursor.X <= bottomRight.X &&
            cursor.Y <= bottomRight.Y;

        if (isOverVideo)
            _overlayWindow.ShowControls();
        else
            _overlayWindow.HideControls();
    }

}