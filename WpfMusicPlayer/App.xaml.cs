using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using WpfMusicPlayer.Helpers;
using WpfMusicPlayer.Services.Abstractions;
using WpfMusicPlayer.Services.Implementations;
using WpfMusicPlayer.ViewModels;

namespace WpfMusicPlayer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{

    private readonly IHost _host;

    public App()
    {
        _host = 
            Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddDebug();
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<NativeLoggerBridge>();

                services.AddSingleton<IConfigProvider, ConfigProvider>();
                services.AddSingleton<ISmtcService, SmtcService>();
                services.AddSingleton<ISongDatabaseService, SongDatabaseService>();
                services.AddSingleton<IPlaylistProvider, PlaylistProvider>();

                services.AddTransient<IFileDialogService, FileDialogService>();
                services.AddTransient<ICommandLineParser, CommandLineParser>();

                services.AddSingleton<MainViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var loggerBridge = _host.Services.GetRequiredService<NativeLoggerBridge>();
        MusicPlayerLibrary.AtlTraceRedirectManager.Init(loggerBridge);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (_host)
        {
            await _host.StopAsync();
        }

        base.OnExit(e);
    }
}