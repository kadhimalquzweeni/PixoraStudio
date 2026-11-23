
using System.Windows;
using PixoraStudio.App.ViewModels;

namespace PixoraStudio.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CurrentPage))
                UpdateEditorChrome(viewModel.CurrentPage);
        };
        UpdateEditorChrome(viewModel.CurrentPage);
    }

    private void UpdateEditorChrome(string currentPage)
    {
        bool isPhotoEditor = currentPage == "Photo Editor";
        SidebarColumn.Width = isPhotoEditor ? new GridLength(0) : new GridLength(220);
        SidebarBorder.Visibility = isPhotoEditor ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Height = isPhotoEditor ? new GridLength(0) : new GridLength(65);
        TopHeaderBorder.Visibility = isPhotoEditor ? Visibility.Collapsed : Visibility.Visible;
    }
}