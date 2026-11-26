using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.App.ViewModels;

public partial class PdfConverterViewModel : ObservableObject
{
    private readonly IPdfRenderingService _pdfRenderingService;
    private readonly IImageProcessingService _imageProcessingService;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _conversionCancellation;
    private int _selectedPageNumber;

    [ObservableProperty]
    private string? filePath;

    [ObservableProperty]
    private string documentName = "No PDF loaded";

    [ObservableProperty]
    private string outputFolder = string.Empty;

    [ObservableProperty]
    private int selectedDpi = 300;

    [ObservableProperty]
    private string selectedFormat = "PNG";

    [ObservableProperty]
    private int jpegQuality = 100;

    [ObservableProperty]
    private BitmapSource? previewImage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string statusMessage = "Load a PDF document to get started.";

    [ObservableProperty]
    private int selectedPageCount;

    public ObservableCollection<PdfPageSelectionViewModel> Pages { get; } = [];
    public IReadOnlyList<int> DpiOptions { get; } = [72, 150, 300, 600];
    public IReadOnlyList<string> FormatOptions { get; } = ["PNG", "JPG"];

    public int PageCount => Pages.Count;
    public bool HasDocument => !string.IsNullOrWhiteSpace(FilePath);
    public bool IsJpeg => string.Equals(SelectedFormat, "JPG", StringComparison.OrdinalIgnoreCase);
    public bool CanConvert => HasDocument && SelectedPageCount > 0 && !IsBusy && !string.IsNullOrWhiteSpace(OutputFolder);

    public int SelectedPageNumber
    {
        get => _selectedPageNumber;
        set
        {
            if (SetProperty(ref _selectedPageNumber, value) && value > 0 && HasDocument)
                _ = RefreshPreviewAsync(value);
        }
    }

    public PdfConverterViewModel(
        IPdfRenderingService pdfRenderingService,
        IImageProcessingService imageProcessingService)
    {
        _pdfRenderingService = pdfRenderingService;
        _imageProcessingService = imageProcessingService;
    }

    [RelayCommand(CanExecute = nameof(CanLoadPdf))]
    private async Task BrowsePdfAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a PDF document",
            Filter = "PDF documents (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            await LoadPdfAsync(dialog.FileName);
    }

    public async Task<bool> LoadPdfAsync(string path)
    {
        if (IsBusy)
            return false;

        IsBusy = true;
        StatusMessage = "Reading PDF document...";
        try
        {
            var info = await Task.Run(() => _pdfRenderingService.GetDocumentInfo(path));
            _previewCancellation?.Cancel();

            FilePath = info.FilePath;
            DocumentName = Path.GetFileName(info.FilePath);
            OutputFolder = Path.GetDirectoryName(info.FilePath) ?? string.Empty;

            Pages.Clear();
            foreach (PdfPageInfo page in info.Pages)
            {
                var item = new PdfPageSelectionViewModel(page);
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(PdfPageSelectionViewModel.IsSelected))
                        UpdateSelectedPageCount();
                };
                Pages.Add(item);
            }

            OnPropertyChanged(nameof(PageCount));
            UpdateSelectedPageCount();

            _selectedPageNumber = Pages.Count > 0 ? 1 : 0;
            OnPropertyChanged(nameof(SelectedPageNumber));
            OnPropertyChanged(nameof(HasDocument));

            if (_selectedPageNumber > 0)
                await RefreshPreviewAsync(_selectedPageNumber);
            else
                PreviewImage = null;

            StatusMessage = $"Loaded {DocumentName} · {PageCount} page(s).";
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not load PDF: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the output folder",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            OutputFolder = dialog.FolderName;
    }

    [RelayCommand]
    private void SelectAllPages()
    {
        foreach (var page in Pages)
            page.IsSelected = true;
        UpdateSelectedPageCount();
    }

    [RelayCommand]
    private void DeselectAllPages()
    {
        foreach (var page in Pages)
            page.IsSelected = false;
        UpdateSelectedPageCount();
    }

    [RelayCommand(CanExecute = nameof(CanConvertPages))]
    private async Task ConvertAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || string.IsNullOrWhiteSpace(OutputFolder))
        {
            StatusMessage = "Choose a PDF document and output folder first.";
            return;
        }

        int[] selectedPages = Pages.Where(page => page.IsSelected)
            .Select(page => page.PageNumber)
            .ToArray();

        if (selectedPages.Length == 0)
            return;

        Directory.CreateDirectory(OutputFolder);
        IsBusy = true;
        Progress = 0;
        StatusMessage = $"Converting {selectedPages.Length} page(s)...";
        _conversionCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _conversionCancellation.Token;

        string sourceName = Path.GetFileNameWithoutExtension(FilePath);
        bool jpeg = IsJpeg;
        SKEncodedImageFormat imageFormat = jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
        string extension = jpeg ? ".jpg" : ".png";
        int dpi = SelectedDpi;
        int quality = JpegQuality;
        string output = OutputFolder;
        IProgress<double> progressReporter = new Progress<double>(value => Progress = value);

        try
        {
            int completed = 0;
            await Task.Run(() =>
                _pdfRenderingService.RenderPages(
                    FilePath,
                    selectedPages,
                    dpi,
                    (pageNumber, bitmap) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        byte[] encoded = _imageProcessingService.EncodeImage(bitmap, imageFormat, quality);
                        string outputPath = Path.Combine(output, $"{sourceName}_page_{pageNumber:D3}{extension}");
                        File.WriteAllBytes(outputPath, encoded);
                        completed++;
                        progressReporter.Report(completed * 100d / selectedPages.Length);
                    },
                    cancellationToken), cancellationToken);

            Progress = 100;
            StatusMessage = $"Converted {completed} page(s) to {extension.TrimStart('.').ToUpperInvariant()}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Conversion cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Conversion failed: {exception.Message}";
        }
        finally
        {
            _conversionCancellation.Dispose();
            _conversionCancellation = null;
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    [RelayCommand]
    private void CancelConversion() => _conversionCancellation?.Cancel();

    private bool CanLoadPdf() => !IsBusy;

    private bool CanConvertPages() => CanConvert;

    private async Task RefreshPreviewAsync(int pageNumber)
    {
        if (string.IsNullOrWhiteSpace(FilePath) || pageNumber < 1)
            return;

        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        string path = FilePath;

        try
        {
            BitmapSource preview = await Task.Run(() =>
            {
                using var rendered = _pdfRenderingService.RenderPage(path, pageNumber, 72);
                float scale = Math.Min(320f / rendered.Width, 440f / rendered.Height);
                if (scale < 1f)
                {
                    int width = Math.Max(1, (int)Math.Round(rendered.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(rendered.Height * scale));
                    using var resized = _imageProcessingService.Resize(rendered, width, height);
                    return CreateBitmapSource(_imageProcessingService.EncodeImage(resized, SKEncodedImageFormat.Png));
                }

                return CreateBitmapSource(_imageProcessingService.EncodeImage(rendered, SKEncodedImageFormat.Png));
            }, cancellation.Token);

            if (!cancellation.IsCancellationRequested && SelectedPageNumber == pageNumber)
                PreviewImage = preview;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
                StatusMessage = $"Preview unavailable: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
                _previewCancellation = null;
            cancellation.Dispose();
        }
    }

    private static BitmapSource CreateBitmapSource(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void UpdateSelectedPageCount()
    {
        SelectedPageCount = Pages.Count(page => page.IsSelected);
        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        BrowsePdfCommand.NotifyCanExecuteChanged();
        ConvertCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CanConvert));
        RefreshCommandStates();
    }

    partial void OnOutputFolderChanged(string value)
    {
        OnPropertyChanged(nameof(CanConvert));
        RefreshCommandStates();
    }

    partial void OnSelectedPageCountChanged(int value) => OnPropertyChanged(nameof(CanConvert));

    partial void OnSelectedFormatChanged(string value) => OnPropertyChanged(nameof(IsJpeg));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConvert));
        RefreshCommandStates();
    }
}

public sealed partial class PdfPageSelectionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isSelected = true;

    public int PageNumber { get; }
    public double WidthPoints { get; }
    public double HeightPoints { get; }

    public string DimensionsLabel => $"{WidthPoints:0.#} × {HeightPoints:0.#} pt";

    public PdfPageSelectionViewModel(PdfPageInfo page)
    {
        PageNumber = page.PageNumber;
        WidthPoints = page.WidthPoints;
        HeightPoints = page.HeightPoints;
    }
}
