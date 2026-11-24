using Microsoft.Extensions.DependencyInjection;
using PixoraStudio.Core.Interfaces;
using PixoraStudio.Infrastructure.Services;
using PixoraStudio.App.ViewModels;
using PixoraStudio.App.Views;
using System.Windows;
using LibVLCSharp.Shared;

namespace PixoraStudio.App;

public partial class App : Application
{
    public static LibVLC LibVLCInstance { get; private set; } = null!;
    private ServiceProvider _serviceProvider = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LibVLCSharp.Shared.Core.Initialize();

        LibVLCInstance = new LibVLC();

        var services = new ServiceCollection();

        services.AddSingleton<IMediaTypeDetector, MediaTypeDetector>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<PhotoEditorViewModel>();
        services.AddSingleton<VideoEditorViewModel>();
        services.AddSingleton<IImageProcessingService, ImageProcessingService>();
        services.AddSingleton<IAiUpscalingService, PixoraAiUpscalingService>();
        services.AddSingleton<IHardwareVideoThumbnailDecoder, MediaFoundationVideoThumbnailDecoder>();
        services.AddSingleton<IHardwareVideoCapabilityService, MediaFoundationHardwareVideoCapabilityService>();
        services.AddSingleton<IVideoProcessingService, VideoProcessingService>();
        services.AddSingleton<IVideoExportService, VideoExportService>();

        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider
            .GetRequiredService<MainWindow>();

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}
