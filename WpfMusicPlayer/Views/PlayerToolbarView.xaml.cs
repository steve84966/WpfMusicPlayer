using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfMusicPlayer.ViewModels;

namespace WpfMusicPlayer.Views;

public partial class PlayerToolbarView : UserControl
{
    public SongInfoView LandscapeSongInfo => InternalLandscapeSongInfo;
    public TranslateTransform LandscapeSongInfoTranslate => (TranslateTransform)InternalLandscapeSongInfo.RenderTransform;
    public StackPanel VolumePanelElement => InternalVolumePanel;

    public PlayerToolbarView()
    {
        InitializeComponent(); 
    }

    private void ProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsDraggingSlider = true;
    }

    private void ProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SeekToCurrentPosition();
    }
}
