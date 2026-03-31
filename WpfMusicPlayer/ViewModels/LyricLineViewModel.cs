using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfMusicPlayer.ViewModels;

public partial class LyricLineViewModel(string text, int timeMs = -1, string? translation = null, string? romanji = null) : ObservableObject
{
    public string Text { get; } = text;

    public int TimeMs { get; } = timeMs;

    public string? Translation { get; } = translation;

    public string? Romanji { get; } = romanji;

    public bool HasTranslation => Translation != null;

    public bool HasRomanji => Romanji != null;

    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }

    public bool IsProgressEnabled { get; init; }

    [ObservableProperty]
    public partial double Progress { get; set; }
}

