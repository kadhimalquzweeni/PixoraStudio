
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using System.Windows;

namespace PixoraStudio.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IMediaTypeDetector _mediaTypeDetector;

    [ObservableProperty]
    private object? currentViewModel;

    [ObservableProperty]
    private string currentPage = "Home";

    public HomeViewModel HomeViewModel { get; }
    public PhotoEditorViewModel PhotoEditorViewModel { get; }
    public VideoEditorViewModel VideoEditorViewModel { get; }
    public PdfConverterViewModel PdfConverterViewModel { get; }

    public MainViewModel(
        IMediaTypeDetector mediaTypeDetector,
        HomeViewModel homeViewModel,
        PhotoEditorViewModel photoEditorViewModel,
        VideoEditorViewModel videoEditorViewModel,
        PdfConverterViewModel pdfConverterViewModel)
    {
        _mediaTypeDetector = mediaTypeDetector;

        HomeViewModel = homeViewModel;
        PhotoEditorViewModel = photoEditorViewModel;
        VideoEditorViewModel = videoEditorViewModel;
        PdfConverterViewModel = pdfConverterViewModel;

        CurrentViewModel = HomeViewModel;
    }

    [RelayCommand]
    private void NavigateHome()
    {
        CurrentPage = "Home";
        CurrentViewModel = HomeViewModel;
    }

    [RelayCommand]
    private void OpenPhotoEditor()
    {
        CurrentPage = "Photo Editor";
        CurrentViewModel = PhotoEditorViewModel;
    }

    [RelayCommand]
    private void OpenVideoEditor()
    {
        CurrentPage = "Video Editor";
        CurrentViewModel = VideoEditorViewModel;
    }

    [RelayCommand]
    private void OpenPdfConverter()
    {
        CurrentPage = "PDF Converter";
        CurrentViewModel = PdfConverterViewModel;
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Media",
            Filter =
                "Supported media|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif;*.mp4;*.mov;*.avi;*.mkv;*.webm;*.mp3;*.wav;*.pdf;*.pixora|" +
                "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|" +
                "Videos|*.mp4;*.mov;*.avi;*.mkv;*.webm|" +
                "Audio|*.mp3;*.wav|" +
                "PDF Documents|*.pdf|" +
                "Pixora Projects|*.pixora|" +
                "All files|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        string filePath = dialog.FileName;

        if (string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            await PdfConverterViewModel.LoadPdfAsync(filePath);
            OpenPdfConverter();
            return;
        }

        MediaType mediaType = _mediaTypeDetector.Detect(filePath);

        switch (mediaType)
        {
            case MediaType.Image:
                PhotoEditorViewModel.LoadImage(filePath);
                OpenPhotoEditor();
                break;

            case MediaType.Video:
                await VideoEditorViewModel.LoadVideoAsync(filePath);
                OpenVideoEditor();
                break;

            case MediaType.Audio:
                MessageBox.Show(
                    "Audio editing will be supported in a future update.",
                    "Pixora Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                break;

            case MediaType.PixoraProject:
                MessageBox.Show(
                    "Project loading will be implemented in a future update.",
                    "Pixora Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                break;

            default:
                MessageBox.Show(
                    "This file format is not supported.",
                    "Pixora Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                break;
        }
    }
}
