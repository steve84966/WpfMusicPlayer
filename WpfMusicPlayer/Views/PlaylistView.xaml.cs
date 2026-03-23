using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfMusicPlayer.ViewModels;

namespace WpfMusicPlayer.Views;

public partial class PlaylistView : UserControl
{
    public PlaylistView()
    {
        InitializeComponent();
    }

    private void PlaylistListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox) return;

        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);

        if (source is ListBoxItem { DataContext: PlaylistItemViewModel item })
        {
            if (DataContext is MainViewModel vm)
                vm.PlayFromPlaylist(item);
        }
    }
}
