using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MusicPlayerLibrary;
using WpfMusicPlayer.Helpers;
using WpfMusicPlayer.Services.Abstractions;

namespace WpfMusicPlayer.ViewModels;

public partial class LyricsViewModel(ILogger<LyricsViewModel> logger, IFileDialogService fileDialogService) : ObservableObject
{
    private LrcFileController? _lrcFileController;

    public event Action<float>? SeekRequested;
    public event Action<string>? UpdateCurrentLyricRequested;

    private float _currentLyricDuration;

    public ObservableCollection<LyricLineViewModel> Lyrics { get; } = [];

    [ObservableProperty]
    public partial int CurrentLyricIndex { get; private set; } = -1;

    [ObservableProperty]
    public partial bool IsTranslationVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool HasTranslationAvailable { get; private set; }

    [ObservableProperty]
    public partial bool IsRomanjiVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool HasRomanjiAvailable { get; private set; }

    [RelayCommand]
    private void ToggleTranslation()
    {
        IsTranslationVisible = !IsTranslationVisible;
    }

    [RelayCommand]
    private void ToggleRomanji()
    {
        IsRomanjiVisible = !IsRomanjiVisible;
    }

    public void SeekToLyric(LyricLineViewModel lyric)
    {
        if (lyric.TimeMs < 0) return;
        logger.LogInformation("Seek to lyric requested, time = {LyricTimeMs} (in MS)", lyric.TimeMs);
        SeekRequested?.Invoke(lyric.TimeMs / 1000f);
    }

    public void ResetState()
    {
        _lrcFileController?.Dispose();
        _lrcFileController = null;
        _currentLyricDuration = 0;
        Lyrics.Clear();
        CurrentLyricIndex = -1;
        HasTranslationAvailable = false;
        HasRomanjiAvailable = false;
    }

    public void UpdateLyricProgress(float time)
    {
        if (_lrcFileController == null) return;
        _lrcFileController.SetTimeStamp((int)(time * 1000));
        var newIndex = _lrcFileController.GetCurrentLrcNodeIndex();

        if (newIndex != CurrentLyricIndex && newIndex >= 0 && newIndex < Lyrics.Count)
        {
            if (CurrentLyricIndex >= 0 && CurrentLyricIndex < Lyrics.Count)
            {
                Lyrics[CurrentLyricIndex].IsHighlighted = false;
                Lyrics[CurrentLyricIndex].Progress = 0;
            }
            CurrentLyricIndex = newIndex;
            Lyrics[CurrentLyricIndex].IsHighlighted = true;
        }

        if (CurrentLyricIndex >= 0 && CurrentLyricIndex < Lyrics.Count
            && Lyrics[CurrentLyricIndex].IsProgressEnabled)
        {
            Lyrics[CurrentLyricIndex].Progress = _lrcFileController.GetLrcPercentage(CurrentLyricIndex);
        }
    }

    public void OnPlaybackStopped()
    {
        if (CurrentLyricIndex < 0 || CurrentLyricIndex >= Lyrics.Count) return;
        Lyrics[CurrentLyricIndex].IsHighlighted = false;
        Lyrics[CurrentLyricIndex].Progress = 0;
        CurrentLyricIndex = 0;
        Lyrics[CurrentLyricIndex].IsHighlighted = true;
        if (_lrcFileController != null && CurrentLyricIndex >= 0 && CurrentLyricIndex < Lyrics.Count
            && Lyrics[CurrentLyricIndex].IsProgressEnabled)
        {
            Lyrics[CurrentLyricIndex].Progress = _lrcFileController.GetLrcPercentage(CurrentLyricIndex);
        }
    }

    public void LoadLyrics(string? filePath, string? id3Lyric, string? songTitle, float songDuration)
    {
        logger.LogInformation("LoadLyrics: loading lyrics for {FilePath}", filePath);
        Lyrics.Clear();
        CurrentLyricIndex = -1;
        HasTranslationAvailable = false;
        HasRomanjiAvailable = false;
        _currentLyricDuration = songDuration;

        if (!string.IsNullOrEmpty(id3Lyric))
        {
            try
            {
                logger.LogInformation("LoadLyrics: found embedded ID3 lyrics");
                ParseAndAddLocalLyric(id3Lyric, songDuration);
                return;
            }
            catch
            {
                // ignored - fallback to lrc file read
            }
        }

        var lrcPath = FindBestLrcFile(filePath, songTitle);
        if (!string.IsNullOrEmpty(lrcPath))
        {
            logger.LogInformation("LoadLyrics: found best match LRC file: {LrcPath}", lrcPath);
            try
            {
                var contentBytes = File.ReadAllBytes(lrcPath);
                var content = LocaleConverter.GetSystemStringFromBytes(contentBytes);
                ParseAndAddLocalLyric(content, songDuration);
                return;
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to load lrc: {ex.Message}", "Error", WpfMessageBoxIcon.Error);
            }
        }

        var exactLrcPath = Path.ChangeExtension(filePath, ".lrc");
        if (exactLrcPath != null && File.Exists(exactLrcPath))
        {
            logger.LogInformation("LoadLyrics: fallback to exact LRC path: {LrcPath}", exactLrcPath);
            try
            {
                var contentBytes = File.ReadAllBytes(exactLrcPath);
                var content = LocaleConverter.GetSystemStringFromBytes(contentBytes);
                ParseAndAddLocalLyric(content, songDuration);
                return;
            }
            catch (InvalidOperationException ex)
            {
                WpfMessageBox.Show(ex.Message, "Error", WpfMessageBoxIcon.Error);
            }
        }

        _lrcFileController = null;
        logger.LogInformation("LoadLyrics: no lyrics found");
        Lyrics.Add(new LyricLineViewModel("暂无歌词"));
    }

    private void ParseAndAddLocalLyric(string content, float songDuration)
    {
        _lrcFileController?.Dispose();
        _lrcFileController = new LrcFileController();

        _lrcFileController.ParseLrcStream(content);
        _lrcFileController.SetSongDuration(songDuration);
        if (!_lrcFileController.Valid()) return;

        var hasTranslation = _lrcFileController.IsAuxiliaryInfoEnabled(LrcAuxiliaryInfo.Translation);
        HasTranslationAvailable = hasTranslation;
        var hasRomanji = _lrcFileController.IsAuxiliaryInfoEnabled(LrcAuxiliaryInfo.Romanization);
        HasRomanjiAvailable = hasRomanji;

        for (var i = 0; i < _lrcFileController.GetLrcNodeCount(); ++i)
        {
            var lyricIndex = _lrcFileController.GetLrcLineAuxIndex(i, LrcAuxiliaryInfo.Lyric);
            var timeMs = _lrcFileController.GetLrcNodeTimeMs(i);
            var lyricText = _lrcFileController.GetLrcLineAt(i, lyricIndex);

            string? translation = null;
            string? romanji = null;
            if (hasTranslation)
            {
                var transIndex = _lrcFileController.GetLrcLineAuxIndex(i, LrcAuxiliaryInfo.Translation);
                if (transIndex >= 0)
                    translation = _lrcFileController.GetLrcLineAt(i, transIndex);
            }
            if (hasRomanji)
            {
                var romanjiIndex = _lrcFileController.GetLrcLineAuxIndex(i, LrcAuxiliaryInfo.Romanization);
                if (romanjiIndex >= 0)
                    romanji = _lrcFileController.GetLrcLineAt(i, romanjiIndex);
            }

            Lyrics.Add(new LyricLineViewModel(lyricText, timeMs, translation, romanji)
            {
                IsProgressEnabled = _lrcFileController.IsPercentageEnabled(i)
            });
        }
    }

    private static string? FindBestLrcFile(string? filePath, string? songTitle)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        var fileDir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(fileDir)) return null;

        var searchPaths = new List<string>
        {
            fileDir,
            Path.GetFullPath(Path.Combine(fileDir, "..")),
        };

        AddKnownPath(Environment.SpecialFolder.MyMusic);
        AddKnownPath(Environment.SpecialFolder.MyDocuments);

        var targetName = songTitle;
        if (string.IsNullOrEmpty(targetName))
        {
            targetName = Path.GetFileNameWithoutExtension(filePath);
        }

        foreach (var dir in searchPaths.Where(Directory.Exists))
        {
            try
            {
                var lrcFiles = Directory.GetFiles(dir, "*.lrc");
                string? bestFile = null;
                var bestSimilarity = 0f;

                foreach (var file in lrcFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);

                    if (fileName.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }

                    var sim = CalculateJaccardSimilarity(fileName, targetName);
                    if (!(sim > 0.7f) || !(sim > bestSimilarity)) continue;
                    bestSimilarity = sim;
                    bestFile = file;
                }

                if (bestFile != null) return bestFile;
            }
            catch
            {
                // ignored
            }
        }

        return null;

        void AddKnownPath(Environment.SpecialFolder folder)
        {
            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(path)) return;
            searchPaths.Add(path);
            searchPaths.Add(Path.Combine(path, "Lyrics"));
        }
    }

    [RelayCommand]
    private async Task LoadCustomLyrics()
    {
        if (_currentLyricDuration == 0) return;
        var path = await fileDialogService.PickLrcAsync();
        if (string.IsNullOrEmpty(path)) return;
        
        try
        {
            var contentBytes = await File.ReadAllBytesAsync(path);
            var content = LocaleConverter.GetSystemStringFromBytes(contentBytes);
            LoadLyrics(null, content, null, _currentLyricDuration);
            UpdateCurrentLyricRequested?.Invoke(content);
        }
        catch (InvalidOperationException ex)
        {
            WpfMessageBox.Show(ex.Message, "Error", WpfMessageBoxIcon.Error);
        }
    }

    [RelayCommand]
    public void AdjustLrcOffset()
    {
        if (_lrcFileController is null) return;
        OffsetAdjustDialog.Show(_lrcFileController.GetLrcOffset(), 
            title: "调整歌词延迟",
            onChanged: offset => _lrcFileController.SetLrcOffsetExt(offset));
    }

    private static float CalculateJaccardSimilarity(string str1, string str2)
    {
        var set1 = new HashSet<char>(str1);
        var set2 = new HashSet<char>(str2);

        var intersection = new HashSet<char>(set1);
        intersection.IntersectWith(set2);

        var union = new HashSet<char>(set1);
        union.UnionWith(set2);

        if (union.Count == 0) return 0f;
        return (float)intersection.Count / union.Count;
    }
    
}



