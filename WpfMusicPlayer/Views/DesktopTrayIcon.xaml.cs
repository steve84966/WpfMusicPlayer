using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using WpfMusicPlayer.ViewModels;

namespace WpfMusicPlayer.Views;

public class DesktopTrayIcon : IDisposable
{
    private readonly TaskbarIcon _notifyIcon;

    public DesktopTrayIcon(DesktopTrayIconViewModel viewModel)
    {
        _notifyIcon = (TaskbarIcon)Application.Current.FindResource("TrayIcon")!;
        _notifyIcon.DataContext = viewModel;
        _notifyIcon.TrayMouseDoubleClick += (_, _) => viewModel.ToggleMainWindow();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DesktopTrayIconViewModel.IsTaskbarIconVisible))
                _notifyIcon.Visibility = viewModel.IsTaskbarIconVisible;
        };
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
        GC.SuppressFinalize(this);
    }
}
