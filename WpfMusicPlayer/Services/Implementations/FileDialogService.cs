using System.Windows;
using WinRT.Interop;
using WpfMusicPlayer.Services.Abstractions;

namespace WpfMusicPlayer.Services.Implementations;

public class FileDialogService : IFileDialogService
{
    public async Task<string?> PickMusicFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".ncm");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<IReadOnlyList<string>> PickMusicFilesAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".ncm");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
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

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickJsonAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> SaveJsonAsync()
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON 文件", new List<string> { ".json" });
        picker.SuggestedFileName = "playlist";

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
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

        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
