namespace WpfMusicPlayer.Services.Abstractions;

public interface IFileDialogService
{
    public List<string> MscExtList { get; }
    Task<string?> PickMusicFileAsync();
    Task<IReadOnlyList<string>> PickMusicFilesAsync();
    Task<string?> PickWpplAsync();
    Task<string?> SaveWpplAsync(string suggestedFileName = "playlist");
    Task<string?> PickImageAsync();
    Task<string?> PickLrcAsync();
}
