namespace WpfMusicPlayer.Services;

public interface IFileDialogService
{
    Task<string?> PickMusicFileAsync();
}
