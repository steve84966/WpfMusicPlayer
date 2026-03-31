using System.Windows.Media.Imaging;

namespace WpfMusicPlayer.ViewModels;

public class PlaylistItemViewModel(string filePath, string title, string artist, int playedCount) : ViewModelBase
{
    public string FilePath { get; } = filePath;
    public string Title { get; } = title;
    public string Artist { get; } = artist;
    // 删除duration，改为播放次数
    public int PlayedCount
    {
        get;
        set => SetProperty(ref field, value);
    } = playedCount;

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
