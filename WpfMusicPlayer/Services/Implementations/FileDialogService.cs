using System.Windows;
using WinRT.Interop;
using WpfMusicPlayer.Services.Abstractions;

namespace WpfMusicPlayer.Services.Implementations;

public class FileDialogService : IFileDialogService
{
    public List<string> ExtensionList => [
        ".mp3", ".flac", ".wav", ".wma", ".m4a", ".aac", ".ogg", ".ape", ".ncm"
    ]; 
    public async Task<string?> PickMusicFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        foreach (var ext in ExtensionList)
        {
            picker.FileTypeFilter.Add(ext);
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? throw new InvalidOperationException()).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<IReadOnlyList<string>> PickMusicFilesAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        foreach (var ext in ExtensionList)
        {
            picker.FileTypeFilter.Add(ext);
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? throw new InvalidOperationException()).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(f => f.Path).ToList() ?? [];
    }

    public async Task<string?> PickImageAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? throw new InvalidOperationException()).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickWpplAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".wppl");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? throw new InvalidOperationException()).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> SaveWpplAsync(string suggestedFileName = "playlist")
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("WpfMusicPlayer 播放列表", new List<string> { ".wppl" });
        picker.SuggestedFileName = suggestedFileName;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? throw new InvalidOperationException()).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickLrcAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".lrc");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? throw new InvalidOperationException()).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
