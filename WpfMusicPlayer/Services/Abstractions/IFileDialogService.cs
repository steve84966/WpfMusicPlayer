namespace WpfMusicPlayer.Services.Abstractions;

public interface IFileDialogService
{
    Task<string?> PickMusicFileAsync();
    Task<IReadOnlyList<string>> PickMusicFilesAsync();
    Task<string?> PickJsonAsync();
    Task<string?> SaveJsonAsync();
    Task<string?> PickImageAsync();
    Task<string?> PickLrcAsync();
}
