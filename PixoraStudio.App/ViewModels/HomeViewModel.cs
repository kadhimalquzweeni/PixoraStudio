
using CommunityToolkit.Mvvm.ComponentModel;

namespace PixoraStudio.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    [ObservableProperty]
    private string welcomeMessage = "Welcome to Pixora Studio";
}
