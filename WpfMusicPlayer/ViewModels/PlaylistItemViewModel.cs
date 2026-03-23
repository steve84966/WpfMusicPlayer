using System.Windows.Media.Imaging;

namespace WpfMusicPlayer.ViewModels;

public class PlaylistItemViewModel(string filePath, string title, string artist, string duration) : ViewModelBase
{
    public string FilePath { get; } = filePath;
    public string Title { get; } = title;
    public string Artist { get; } = artist;
    public string Duration { get; } = duration;

    public BitmapImage? AlbumCover
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsPlaying
    {
        get;
        set => SetProperty(ref field, value);
    }
}
