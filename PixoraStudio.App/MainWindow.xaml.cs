
using System.Windows;
using PixoraStudio.App.ViewModels;

namespace PixoraStudio.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}