
using LibVLCSharp.Shared;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PixoraStudio.App.ViewModels;

namespace PixoraStudio.App.Views;

public partial class VideoEditorView : UserControl
{
    private MediaPlayer? _mediaPlayer;
    private LibVLC? _libVLC;

    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    private bool _isSeeking;

    public VideoEditorView()
    {
        InitializeComponent();

        Loaded += VideoEditorView_Loaded;
        Unloaded += VideoEditorView_Unloaded;

        _timer.Tick += Timer_Tick;
    }

    private void VideoEditorView_Loaded(
        object sender, RoutedEventArgs e)
    {
        _libVLC = App.LibVLCInstance;
        _mediaPlayer = new MediaPlayer(_libVLC);

        VideoPlayer.MediaPlayer = _mediaPlayer;

        if (DataContext is VideoEditorViewModel vm)
        {
            vm.VideoLoaded += OnVideoLoaded;
            vm.PlayPauseRequested += OnPlayPause;
            vm.SeekRequested += OnSeek;
            vm.VolumeChanged += OnVolumeChanged;

            _mediaPlayer.Volume = (int)vm.Volume;
        }

        _timer.Start();
    }

    private void OnVideoLoaded(string filePath)
    {
        if (_mediaPlayer == null || _libVLC == null)
            return;

        using var media = new Media(
            _libVLC, filePath, FromType.FromPath);

        _mediaPlayer.Play(media);
    }

    private void OnPlayPause()
    {
        if (_mediaPlayer == null)
            return;

        if (_mediaPlayer.IsPlaying)
            _mediaPlayer.Pause();
        else
            _mediaPlayer.Play();
    }

    private void OnSeek(double position)
    {
        if (_mediaPlayer == null)
            return;

        _mediaPlayer.Time = (long)position;
    }

    private void OnVolumeChanged(double value)
    {
        if (_mediaPlayer == null)
            return;

        _mediaPlayer.Volume = (int)value;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_mediaPlayer == null ||
            DataContext is not VideoEditorViewModel vm)
            return;

        vm.IsPlaying = _mediaPlayer.IsPlaying;

        if (!_isSeeking && _mediaPlayer.Length > 0)
        {
            vm.PlaybackPosition = _mediaPlayer.Time;
            vm.CurrentTime = TimeSpan
                .FromMilliseconds(_mediaPlayer.Time)
                .ToString(@"mm\:ss");
        }
    }

    private void Timeline_MouseDown(
        object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isSeeking = true;
    }

    private void Timeline_MouseUp(
        object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isSeeking = false;

        if (DataContext is VideoEditorViewModel vm)
            vm.SeekCommand.Execute(vm.PlaybackPosition);
    }

    private void VideoEditorView_Unloaded(
        object sender, RoutedEventArgs e)
    {
        _timer.Stop();

        if (DataContext is VideoEditorViewModel vm)
        {
            vm.VideoLoaded -= OnVideoLoaded;
            vm.PlayPauseRequested -= OnPlayPause;
            vm.SeekRequested -= OnSeek;
            vm.VolumeChanged -= OnVolumeChanged;
        }

        _mediaPlayer?.Stop();
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
    }

    private void VolumeSlider_ValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is VideoEditorViewModel vm)
            vm.ChangeVolumeCommand.Execute(e.NewValue);
    }
}