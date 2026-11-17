
using LibVLCSharp.Shared;
using PixoraStudio.App.ViewModels;
using PixoraStudio.App.Windows;
using PixoraStudio.App.Services;
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
    private CancellationTokenSource? _audioLoadCancellation;
    private bool _usesCustomAacAudio;
    private bool _isPreparingVideo;
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
        vm.SeekRequested -= OnSeek;
        vm.VolumeChanged -= OnVolumeChanged;

        vm.VideoLoaded += OnVideoLoaded;
        vm.VideoClosed += OnVideoClosed;
        vm.PlayPauseRequested += OnPlayPause;
        vm.SeekRequested += OnSeek;
        vm.VolumeChanged += OnVolumeChanged;

        if (_mediaPlayer != null)
            _mediaPlayer.Volume = (int)vm.Volume;
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

    private async void OnVideoLoaded(string filePath)
    {
        if (_mediaPlayer == null || _libVLC == null)
            return;

        int loadVersion = ++_videoLoadVersion;
        _isPreparingVideo = true;
        _audioLoadCancellation?.Cancel();
        _audioLoadCancellation?.Dispose();
        _audioLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _audioLoadCancellation.Token;

        _mediaPlayer.Stop();
        _mediaPlayer.Mute = false;
        _usesCustomAacAudio = false;

        try
        {
            // Decode AAC before starting video so both playback clocks begin together.
            // Unsupported/non-MP4 audio falls back to LibVLC's built-in audio.
            _usesCustomAacAudio = await _aacAudioPlaybackService.LoadAsync(
                filePath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (loadVersion == _videoLoadVersion)
                _isPreparingVideo = false;
            return;
        }
        catch (Exception ex)
        {
            if (loadVersion != _videoLoadVersion)
                return;

            _aacAudioPlaybackService.Unload();
            System.Diagnostics.Debug.WriteLine(
                $"Custom AAC playback unavailable; using LibVLC audio: {ex.Message}");
        }

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

        _mediaPlayer.Mute = _usesCustomAacAudio;
        _mediaPlayer.Play(media);

        if (_usesCustomAacAudio)
        {
            try
            {
                _aacAudioPlaybackService.Volume = (float)(DataContext is VideoEditorViewModel vm
                    ? vm.Volume / 100d
                    : 1d);
                _aacAudioPlaybackService.Play();
            }
            catch (Exception ex)
            {
                _aacAudioPlaybackService.Unload();
                _usesCustomAacAudio = false;
                _mediaPlayer.Mute = false;
                System.Diagnostics.Debug.WriteLine(
                    $"Custom AAC output could not start; using LibVLC audio: {ex.Message}");
            }
        }
    }

    private void OnVideoClosed()
    {
        _videoLoadVersion++;
        _isPreparingVideo = false;
        _audioLoadCancellation?.Cancel();
        _audioLoadCancellation?.Dispose();
        _audioLoadCancellation = null;
        _aacAudioPlaybackService.Unload();
        _usesCustomAacAudio = false;

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Mute = false;
        }
    }

    private void OnPlayPause()
    {
        if (_isPreparingVideo || _mediaPlayer == null)
            return;

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            if (_usesCustomAacAudio)
                _aacAudioPlaybackService.Pause();
        }
        else
        {
            _mediaPlayer.Play();
            if (_usesCustomAacAudio)
                _aacAudioPlaybackService.Play();
        }
    }

    private void OnSeek(double position)
    {
        if (_isPreparingVideo || _mediaPlayer == null)
            return;

        bool resumeAudio = _mediaPlayer.IsPlaying;
        _mediaPlayer.Time = (long)position;

        if (_usesCustomAacAudio)
        {
            _aacAudioPlaybackService.Seek(
                TimeSpan.FromMilliseconds(position),
                resumeAudio);
        }
    }

    private void OnVolumeChanged(double value)
    {
        if (_mediaPlayer == null)
            return;

        if (_usesCustomAacAudio)
            _aacAudioPlaybackService.Volume = (float)(value / 100d);
        else
            _mediaPlayer.Volume = (int)value;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_mediaPlayer == null ||
            DataContext is not VideoEditorViewModel vm)
        {
            return;
        }

        vm.IsPlaying = _mediaPlayer.IsPlaying;

        if (!_isDraggingPlayhead && _mediaPlayer.Length > 0)
        {
            vm.PlaybackPosition = _mediaPlayer.Time;

            vm.CurrentTime = FormatPlaybackTime(
                TimeSpan.FromMilliseconds(_mediaPlayer.Time));
        }

        if (_usesCustomAacAudio && _mediaPlayer.IsPlaying)
        {
            _aacAudioPlaybackService.Synchronise(
                TimeSpan.FromMilliseconds(_mediaPlayer.Time));
        }

        UpdateTrimTimeline();
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

            if (_mediaPlayer != null)
                _mediaPlayer.Time = (long)vm.PlaybackPosition;
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

        double position = Math.Clamp(
            x / width * vm.TotalMilliseconds,
            0,
            vm.TotalMilliseconds);

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

            if (_mediaPlayer != null)
                _mediaPlayer.Time = (long)vm.PlaybackPosition;
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

            if (_wasPlayingBeforeScrubbing)
            {
                _mediaPlayer?.Play();
                if (_usesCustomAacAudio)
                    _aacAudioPlaybackService.Play();
            }
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
        double position = Math.Clamp(
            x / width * vm.TotalMilliseconds,
            0,
            vm.TotalMilliseconds);

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

        double startX =
            vm.TrimStart / vm.TotalMilliseconds * width;

        double endX =
            vm.TrimEnd / vm.TotalMilliseconds * width;

        double playheadX =
            vm.PlaybackPosition / vm.TotalMilliseconds * width;

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
        _timer.Stop();
        _overlayPositionTimer.Stop();

        _videoLoadVersion++;
        _isPreparingVideo = false;
        _audioLoadCancellation?.Cancel();
        _audioLoadCancellation?.Dispose();
        _audioLoadCancellation = null;

        _aacAudioPlaybackService.Unload();
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

        _mediaPlayer?.Stop();
        _mediaPlayer?.Dispose();

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