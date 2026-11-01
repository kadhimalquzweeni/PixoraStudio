
using PixoraStudio.App.ViewModels;
using PixoraStudio.Core.Models;
using System.Windows;
using System.Windows.Controls;

namespace PixoraStudio.App.Views;

public partial class PhotoEditorView : UserControl
{
    public PhotoEditorView()
    {
        InitializeComponent();
    }

    private PhotoEditorViewModel? ViewModel =>
        DataContext as PhotoEditorViewModel;

    private void Grayscale_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
            return;

        ViewModel.SelectedFilter = ImageFilter.Grayscale;
    }

    private void Sepia_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
            return;

        ViewModel.SelectedFilter = ImageFilter.Sepia;
    }
}
