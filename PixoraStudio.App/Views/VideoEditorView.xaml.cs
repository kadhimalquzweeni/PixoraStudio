
using LibVLCSharp.Shared;
using PixoraStudio.App.ViewModels;
using PixoraStudio.App.Windows;
using PixoraStudio.App.Services;
using PixoraStudio.Core.Models;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using System.Runtime.InteropServices;

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
    private H264Mp4FrameNavigationTarget? _pendingFrameStepTarget;
    private CancellationTokenSource? _videoLoadCancellation;
    private bool _usesCustomAacAudio;
    private bool _isPreparingVideo;
    private bool _resumePlaybackAfterSeek;
    private int _seekOperationVersion;
    private int _videoLoadVersion;
    private LibVLC? _libVLC;

    private VideoOverlayWindow? _overlayWindow;
    private Visibility _previousSidebarVisibility;
    private Visibility _previousHeaderVisibility;
    private GridLength _previousSidebarColumnWidth;
    private GridLength _previousHeaderRowHeight;
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    // Keeps the separate overlay aligned with the video preview.
    private readonly DispatcherTimer _overlayPositionTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };

    private bool _wasPlayingBeforeScrubbing;
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
        _overlayPositionTimer.Start();

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
        vm.FrameStepRequested -= OnFrameStepRequested;
        vm.SeekRequested -= OnSeek;
        vm.VolumeChanged -= OnVolumeChanged;
        vm.MuteChanged -= OnMuteChanged;

        vm.VideoLoaded += OnVideoLoaded;
        vm.VideoClosed += OnVideoClosed;
        vm.PlayPauseRequested += OnPlayPause;
        vm.FrameStepRequested += OnFrameStepRequested;
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
        vm.FrameStepRequested -= OnFrameStepRequested;
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

        _overlayWindow.PreviewMouseLeftButtonDown +=
            OverlayWindow_PreviewMouseLeftButtonDown;

        _overlayWindow.Show();

        UpdateOverlayPosition();
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
        if (IsPlaybackControl(e.OriginalSource as DependencyObject))
            return;

        if (e.ClickCount == 2)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
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
        UpdateOverlayPosition();
        CheckVideoHover();
    }

    private void UpdateOverlayPosition()
    {
        if (_overlayWindow == null ||
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

        _overlayWindow.Left = topLeft.X;
        _overlayWindow.Top = topLeft.Y;
        _overlayWindow.Width = bottomRight.X - topLeft.X;
        _overlayWindow.Height = bottomRight.Y - topLeft.Y;
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

    private async void OnVideoLoaded(string filePath)
    {
        if (_mediaPlayer == null || _libVLC == null)
            return;

        int loadVersion = ++_videoLoadVersion;
        Interlocked.Increment(ref _seekOperationVersion);
        _resumePlaybackAfterSeek = false;
        _isPreparingVideo = true;
        _videoLoadCancellation?.Cancel();
        _videoLoadCancellation?.Dispose();
        _videoLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _videoLoadCancellation.Token;

        _mediaPlayer.Stop();
        _mediaPlayer.Mute = false;
        _usesManagedH264Video = false;
        _pendingFrameStepTarget = null;
        if (DataContext is VideoEditorViewModel loadingVm)
            loadingVm.CanStepFrames = false;
        ManagedVideoFrame.Source = null;
        ManagedVideoFrame.Visibility = Visibility.Collapsed;

        // Do not decode the complete AAC track before playback. LibVLC can
        // stream the MP4's audio immediately while the optional managed video
        // decoder prepares frames independently in the background.
        _aacAudioPlaybackService.Unload();
        _usesCustomAacAudio = false;

        if (loadVersion != _videoLoadVersion ||
            cancellationToken.IsCancellationRequested ||
            _mediaPlayer == null ||
            _libVLC == null)
        {
            return;
        }

        _isPreparingVideo = false;

        using var media = new Media(
            _libVLC,
            filePath,
            FromType.FromPath);

        if (DataContext is VideoEditorViewModel audioVm)
            ApplyAudioOutputSettings(audioVm);
        _mediaPlayer.Play(media);

        try
        {
            _usesManagedH264Video = await _managedH264PlaybackService.TryStartAsync(
                filePath,
                () => (_mediaPlayer?.Time ?? 0) / 1000d,
                bitmap =>
                {
                    H264Mp4FrameNavigationTarget? presentedFrame =
                        _managedH264PlaybackService.LastPresentedFrame;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ManagedVideoFrame.Source = bitmap;
                        ManagedVideoFrame.Visibility = Visibility.Visible;

                        if (_pendingFrameStepTarget is not null &&
                            _pendingFrameStepTarget == presentedFrame)
                        {
                            _pendingFrameStepTarget = null;
                        }
                    }));
                },
                exception => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _usesManagedH264Video = false;
                    _pendingFrameStepTarget = null;
                    if (DataContext is VideoEditorViewModel failedVm)
                        failedVm.CanStepFrames = false;
                    ManagedVideoFrame.Source = null;
                    ManagedVideoFrame.Visibility = Visibility.Collapsed;
                    System.Diagnostics.Debug.WriteLine(
                        $"Managed H.264 preview stopped; LibVLC fallback remains active: {exception.Message}");
                })),
                cancellationToken);

            if (DataContext is VideoEditorViewModel loadedVm)
                loadedVm.CanStepFrames = _usesManagedH264Video;

            if (!_usesManagedH264Video)
                ManagedVideoFrame.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            return;
        }

    }

    private void OnVideoClosed()
    {
        _videoLoadVersion++;
        Interlocked.Increment(ref _seekOperationVersion);
        _resumePlaybackAfterSeek = false;
        _isPreparingVideo = false;
        _videoLoadCancellation?.Cancel();
        _videoLoadCancellation?.Dispose();
        _videoLoadCancellation = null;
        _aacAudioPlaybackService.Unload();
        _ = _managedH264PlaybackService.StopAsync();
        _usesManagedH264Video = false;
        _pendingFrameStepTarget = null;
        if (DataContext is VideoEditorViewModel closedVm)
            closedVm.CanStepFrames = false;
        ManagedVideoFrame.Source = null;
        ManagedVideoFrame.Visibility = Visibility.Collapsed;
        _usesCustomAacAudio = false;

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Mute = false;
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
        _pendingFrameStepTarget = null;
        bool resumePlayback = _mediaPlayer.IsPlaying ||
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

        if (resumePlayback)
            _mediaPlayer.Pause();

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
                if (DataContext is VideoEditorViewModel failedSeekVm)
                    failedSeekVm.CanStepFrames = false;
                _pendingFrameStepTarget = null;
                ManagedVideoFrame.Source = null;
                ManagedVideoFrame.Visibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine(
                    $"Managed H.264 seek failed; LibVLC fallback remains active: {exception.Message}");
            }
        }

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

    private async void OnFrameStepRequested(int direction)
    {
        if (_isPreparingVideo ||
            _mediaPlayer is null ||
            !_usesManagedH264Video ||
            DataContext is not VideoEditorViewModel vm ||
            direction is not (-1 or 1))
        {
            return;
        }

        H264Mp4FrameNavigationDirection navigationDirection = direction < 0
            ? H264Mp4FrameNavigationDirection.Previous
            : H264Mp4FrameNavigationDirection.Next;

        // Prefer the pending target so rapid repeated clicks advance from the
        // requested frame even while its decode is still in progress.
        H264Mp4FrameNavigationTarget? currentFrame =
            _pendingFrameStepTarget ?? _managedH264PlaybackService.LastPresentedFrame;

        H264Mp4FrameNavigationTarget? target = currentFrame is not null
            ? _managedH264PlaybackService.FindAdjacentFrame(
                currentFrame, navigationDirection)
            : _managedH264PlaybackService.FindAdjacentFrame(
                Math.Max(0, _mediaPlayer.Time / 1000d), navigationDirection);

        if (target is null)
            return;

        _pendingFrameStepTarget = target;
        Interlocked.Increment(ref _seekOperationVersion);
        _resumePlaybackAfterSeek = false;
        _wasPlayingBeforeScrubbing = false;

        // Frame stepping is an explicit paused-preview operation. Do not
        // resume either audio or video after the requested frame is shown.
        if (_mediaPlayer.IsPlaying)
            _mediaPlayer.Pause();
        if (_usesCustomAacAudio)
            _aacAudioPlaybackService.Pause();

        vm.IsPlaying = false;
        double targetMilliseconds = Math.Max(
            0, target.PresentationTimeSeconds * 1000d);
        vm.PlaybackPosition = targetMilliseconds;
        vm.CurrentTime = FormatPlaybackTime(
            TimeSpan.FromMilliseconds(targetMilliseconds));

        _mediaPlayer.Time = (long)Math.Round(
            targetMilliseconds, MidpointRounding.AwayFromZero);

        try
        {
            await _managedH264PlaybackService.SeekToFrameAsync(target);

            if (_pendingFrameStepTarget == target &&
                _managedH264PlaybackService.LastPresentedFrame == target)
            {
                _pendingFrameStepTarget = null;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer frame step, seek, or close superseded this request.
        }
        catch (Exception exception)
        {
            _usesManagedH264Video = false;
            vm.CanStepFrames = false;
            _pendingFrameStepTarget = null;
            ManagedVideoFrame.Source = null;
            ManagedVideoFrame.Visibility = Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine(
                $"Managed H.264 frame stepping failed; LibVLC fallback remains active: {exception.Message}");
        }
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
        if (!_isDraggingPlayhead)
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

        if (_usesCustomAacAudio &&
            _mediaPlayer.IsPlaying &&
            !_aacAudioPlaybackService.IsSeeking)
        {
            _ = SynchroniseAudioAsync(
                TimeSpan.FromMilliseconds(_mediaPlayer.Time));
        }

        UpdateTrimTimeline();
    }

    private async Task SynchroniseAudioAsync(TimeSpan videoPosition)
    {
        int seekVersion = Volatile.Read(ref _seekOperationVersion);
        bool resumeVideo = _mediaPlayer?.IsPlaying == true;

        if (resumeVideo)
            _mediaPlayer?.Pause();

        try
        {
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
            if (resumeVideo &&
                seekVersion == Volatile.Read(ref _seekOperationVersion) &&
                _mediaPlayer is not null)
            {
                _mediaPlayer.Play();
            }
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
            {
                _mediaPlayer?.Pause();
                if (_usesCustomAacAudio)
                    _aacAudioPlaybackService.Pause();
            }

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

                _overlayWindow?.Show();
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
                    _overlayWindow.Show();

                    if (_overlayWindow.IsMouseOver)
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

    private void VideoEditorView_Unloaded(
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
        _ = _managedH264PlaybackService.StopAsync();
        _usesManagedH264Video = false;
        ManagedVideoFrame.Source = null;
        ManagedVideoFrame.Visibility = Visibility.Collapsed;
        _usesCustomAacAudio = false;

        if (_isFullScreen)
            ExitFullScreen();

        UnsubscribeFromViewModel();

        if (_overlayWindow != null)
        {
            _overlayWindow.FullscreenRequested -=
                OverlayWindow_FullscreenRequested;


            _overlayWindow.PreviewMouseLeftButtonDown -=
                OverlayWindow_PreviewMouseLeftButtonDown;

            _overlayWindow.Close();
            _overlayWindow = null;
        }

        if (_editorWindow != null)
        {
            _editorWindow.PreviewKeyDown -=
                FullScreenWindow_PreviewKeyDown;
        }

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.EndReached -= MediaPlayer_EndReached;
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
        }

        _mediaPlayer = null;

        VideoPlayer.MediaPlayer = null;
    }

    private void CheckVideoHover()
    {
        if (_overlayWindow == null ||
            !VideoHost.IsVisible)
        {
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