using MusicPlayerLibrary;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime; 
using System.Security.Cryptography;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WpfMusicPlayer.Helpers;
using WpfMusicPlayer.Models;
using WpfMusicPlayer.Services.Abstractions;
using static WpfMusicPlayer.Models.ConfigData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WpfMusicPlayer.ViewModels;

public enum ActiveView { Player, Playlist, Settings }

public enum PlayMode { Sequential, ListLoop, SingleLoop, Shuffle }

// 重构：全面迁移至CommunityToolkit.MVVM
public partial class MainViewModel : ObservableObject, IDisposable
{
    // 业务逻辑在这里写
    // 不要把业务逻辑写在View里！！！
    // 这里不要直接操作UI！！！
    private readonly IConfigProvider _configProvider;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISmtcService _smtcService;
    private readonly ISongDatabaseService _songDatabase;
    private readonly ICommandLineParser _commandLineParser;
    private readonly IPlaylistProvider _playlistProvider;
    private readonly SynchronizationContext _syncContext;
    private readonly Random _shuffleRandom = new();
    private readonly Stack<string> _shuffleHistory = new();
    private readonly object _openFileLock = new();
    private MusicPlayer _musicPlayer;
    private string? _currentFilePath;
    private string? _currentMd5;
    private LrcFileController? _lrcFileController;
    private int _sampleRate;
    private bool _enableAutoPlay;
    private float? _pendingSeekTime;
    private GCLatencyMode _previousLatencyMode;
    private bool _isRestoredFromCommandLine;
    private bool _isPlaylistUserOpened;
    private bool _isPlaylistDirty;
    private bool _disableAutoAdvance;

    public MainViewModel(
        IConfigProvider configProvider, 
        IFileDialogService fileDialogService, 
        ISmtcService smtcService, 
        ISongDatabaseService songDatabase, 
        ICommandLineParser commandLineParser,
        IPlaylistProvider playlistProvider)
    {
        _configProvider = configProvider;
        _fileDialogService = fileDialogService;
        _smtcService = smtcService;
        _songDatabase = songDatabase;
        _commandLineParser = commandLineParser;
        _playlistProvider = playlistProvider;
        _syncContext = SynchronizationContext.Current!;
        Equalizer = new EqualizerViewModel(ApplyEqualizerBand);
        Settings = new SettingsViewModel(configProvider);
        Settings.SettingChanged += OnSettingChanged;
        CurrentBackgroundMode = configProvider.GetConfig().UI.Background;
        _sampleRate = 48000; // Studio quality
        _musicPlayer = new MusicPlayer(_sampleRate);

        SubscribePlayerEvents();
        SubscribeSmtcEvents();
        RestoreSettingsFromCommandLine();

    }

    private void RestoreSettingsFromCommandLine()
    {
        Volume = _commandLineParser.Volume;

        if (string.IsNullOrEmpty(_commandLineParser.FilePath))
        {
            ActiveView = _commandLineParser.StartupView;
            return;
        }

        IsTranslationVisible = _commandLineParser.TranslationToggled;
        IsRomanjiVisible = _commandLineParser.RomanjiToggled;

        if (_commandLineParser.AppliedEqualizerSettings.Length > 0)
        {
            for (var i = 0; i < _commandLineParser.AppliedEqualizerSettings.Length && i < Equalizer.Bands.Count; i++)
            {
                Equalizer.Bands[i].Value = _commandLineParser.AppliedEqualizerSettings[i];
            }
        }

        _enableAutoPlay = _commandLineParser.AutoStart;
        if (_commandLineParser.MusicCurrentTime > 0)
            _pendingSeekTime = _commandLineParser.MusicCurrentTime;

        ActiveView = _commandLineParser.StartupView;
        if (ActiveView != ActiveView.Player)
        {
            _isRestoredFromCommandLine = true;
        }

        OpenFile(_commandLineParser.FilePath);
    }

    public EqualizerViewModel Equalizer { get; }

    private void ApplyEqualizerBand(int index, int value)
    {
        _musicPlayer.SetEqualizerBand(index, value);
    }

    [ObservableProperty]
    public partial string SongTitle { get; private set; } = "Unknown Title";

    [ObservableProperty]
    public partial string ArtistName { get; private set; } = "Unknown Artist";

    [ObservableProperty]
    public partial BitmapImage? AlbumCoverImage { get; private set; }

    [ObservableProperty]
    public partial string CurrentTime { get; private set; } = "0:00";

    [ObservableProperty]
    public partial string TotalTime { get; private set; } = "0:00";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial double ProgressMaximum { get; private set; } = 100;

    public double Volume
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _musicPlayer.SetMasterVolume((float)value);
            }
        }
    } = 0.5;

    [ObservableProperty]
    public partial string PlayPauseContent { get; private set; } = "\u25B6";

    public ObservableCollection<LyricLineViewModel> Lyrics { get; } = [];

    [ObservableProperty]
    public partial int CurrentLyricIndex { get; private set; } = -1;

    public bool IsDraggingSlider { get; set; }

    [ObservableProperty]
    public partial bool IsTranslationVisible { get; private set; } = true;

    [ObservableProperty]
    public partial bool HasTranslationAvailable { get; private set; }

    [ObservableProperty]
    public partial bool IsRomanjiVisible { get; private set; } = true;

    [ObservableProperty]
    public partial bool HasRomanjiAvailable { get; private set; }

    public bool IsFileDialogOpen { get; set; }

    public bool IsDecoding
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    [ObservableProperty]
    public partial float[] SpectrumData { get; private set; } = [];

    public ObservableCollection<PlaylistItemViewModel> PlaylistItems { get; } = [];

    public string PlaylistTitle
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            _playlistProvider.GetPlaylist().Name = value;
                
            _isPlaylistUserOpened = true;
            _isPlaylistDirty = true;
        }
    } = "播放列表";

    [ObservableProperty]
    public partial BitmapImage? PlaylistCoverImage { get; private set; }

    [ObservableProperty]
    public partial ActiveView ActiveView { get; set; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    public partial UISettings.BackgroundMode CurrentBackgroundMode { get; private set; }

    public PlayMode CurrentPlayMode
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value)) return;
            OnPropertyChanged(nameof(PlayModeContent));
            OnPropertyChanged(nameof(PlayModeTooltip));
        }
    }

    public string PlayModeContent => CurrentPlayMode switch
    {
        PlayMode.Sequential => "\u27A1",
        PlayMode.ListLoop => "\U0001F501",
        PlayMode.SingleLoop => "\U0001F502",
        PlayMode.Shuffle => "\U0001F500",
        _ => "\u27A1"
    };

    public string PlayModeTooltip => CurrentPlayMode switch
    {
        PlayMode.Sequential => "顺序播放",
        PlayMode.ListLoop => "列表循环",
        PlayMode.SingleLoop => "单曲循环",
        PlayMode.Shuffle => "随机播放",
        _ => "顺序播放"
    };

    // for RebootApplication to build command line args
    public bool IsMusicPlaying => _musicPlayer.IsPlaying();

    public void OpenFile(string filePath)
    {
        // 避免对_musicPlayer的重复析构
        lock (_openFileLock)
        {
            if (ActiveView != ActiveView.Player
                && ActiveView != ActiveView.Playlist
                && !_isRestoredFromCommandLine)
            {
                _isRestoredFromCommandLine = false;
                _syncContext.Send(_ => ActiveView = ActiveView.Player, null);
            }


            var isNcm = Path.GetExtension(filePath).Equals(".ncm", StringComparison.OrdinalIgnoreCase);
            if (isNcm)
            {
                _syncContext.Send(_ => IsDecoding = true, null);
            }
            try
            {
                _currentFilePath = filePath;
                _currentMd5 = ComputeFileMd5(filePath);
                _musicPlayer.Dispose();
                _musicPlayer = new MusicPlayer(_sampleRate);
                SubscribePlayerEvents();
                _musicPlayer.OpenFile(filePath);
                if (!_enableAutoPlay)
                {
                    _syncContext.Post(_ => PlayPauseContent = "\u25B6", null);
                }
            }
            catch
            {
                _syncContext.Send(_ => IsDecoding = false, null);
                throw;
            }
        }
    }

    public async void SeekToCurrentPosition()
    {
        if (!_musicPlayer.IsInitialized()) return;
        var timeInSec = _musicPlayer.GetMusicTimeLength();
        var targetTime = timeInSec * (float)ProgressValue / (float)ProgressMaximum;
        var isPlaying = _musicPlayer.IsPlaying();

        IsDraggingSlider = true;

        await Task.Run(() =>
        {
            _musicPlayer.SeekToPosition(targetTime, true);
        });

        if (isPlaying)
        {
            _musicPlayer.Start();
        }

        await Task.Delay(200);
        IsDraggingSlider = false;
        if (!isPlaying)
        {
            ProgressValue = targetTime;
            CurrentTime = FormatTime(targetTime);
        }
        UpdateLyricProgress(targetTime);
    }

    public async void SeekToLyric(LyricLineViewModel lyric)
    {
        if (!_musicPlayer.IsInitialized() || lyric.TimeMs < 0) return;

        var targetTimeSec = lyric.TimeMs / 1000f;
        var isPlaying = _musicPlayer.IsPlaying();

        IsDraggingSlider = true;

        await Task.Run(() =>
        {
            _musicPlayer.SeekToPosition(targetTimeSec, true);
        });

        if (isPlaying)
        {
            _musicPlayer.Start();
        }

        await Task.Delay(200);
        IsDraggingSlider = false;
        if (!isPlaying)
        {
            ProgressValue = targetTimeSec;
            CurrentTime = FormatTime(targetTimeSec);
        }
        UpdateLyricProgress(targetTimeSec);
    }

    public void OnWindowClosed()
    {
        if (_musicPlayer.IsInitialized())
        {
            _musicPlayer.Stop();
        }
    }

    // 当用户打开了播放列表时，用于标记是否保存当前播放列表
    public bool HasUnsavedPlaylistChanges => _isPlaylistUserOpened && _isPlaylistDirty;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.SettingName == nameof(SettingsViewModel.SelectedBackground))
        {
            CurrentBackgroundMode = _configProvider.GetConfig().UI.Background;
        }
    }

    public void PollSpectrumData()
    {
        if (!_musicPlayer.IsInitialized() || !_musicPlayer.IsPlaying())
        {
            if (SpectrumData.Length > 0)
                SpectrumData = [];
            return;
        }
        var data = _musicPlayer.GetAudioFFTData();
        if (data is { Length: > 0 })
            SpectrumData = data;
    }

    public void Dispose()
    {
        Settings.SettingChanged -= OnSettingChanged;
        GCSettings.LatencyMode = _previousLatencyMode;
        _musicPlayer.Dispose();
        _songDatabase.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task SetSampleRate(int sampleRate)
    {
        if (sampleRate < 8000 // Telephone quality
            || sampleRate > 192000) // Above high-resolution audio
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be between 8000 and 192000 Hz.");
        }
        _sampleRate = sampleRate;
        if (_currentFilePath != null)
        {
            OpenFile(_currentFilePath);
        }
    }

    private void SubscribePlayerEvents()
    {
        _musicPlayer.OnPlayerTimeChange += OnTimeChanged;
        _musicPlayer.OnPlayerFileInit += OnFileInit;
        _musicPlayer.OnPlayerAlbumArtInit += OnAlbumArtInit;
        _musicPlayer.OnPlayerStart += OnStart;
        _musicPlayer.OnPlayerPause += OnPause;
        _musicPlayer.OnPlayerStop += OnStop;
    }

    private void SubscribeSmtcEvents()
    {
        _smtcService.PlayRequested += () => _syncContext.Post(_ =>
        {
            if (!_musicPlayer.IsPlaying()) PlayPause();
        }, null);
        _smtcService.PauseRequested += () => _syncContext.Post(_ =>
        {
            if (_musicPlayer.IsPlaying()) PlayPause();
        }, null);
        _smtcService.NextRequested += () => _syncContext.Post(_ => NextSongCommand.Execute(null), null);
        _smtcService.PreviousRequested += () => _syncContext.Post(_ => PrevSongCommand.Execute(null), null);
    }

    private void OnFileInit()
    {
        _syncContext.Post(_ =>
        {
            IsDecoding = false;
            var length = _musicPlayer.GetMusicTimeLength();
            ProgressMaximum = length;
            TotalTime = FormatTime(length);

            var cached = _currentMd5 is not null ? _songDatabase.FindByMd5(_currentMd5) : null;
            if (cached is not null)
            {
                // 优先使用数据库缓存
                SongTitle = cached.Title;
                ArtistName = cached.Artist;
            }
            else
            {
                SongTitle = _musicPlayer.GetSongTitle() ?? "Unknown Title";
                ArtistName = _musicPlayer.GetSongArtist() ?? "Unknown Artist";

                if (_currentMd5 is not null)
                {
                    _songDatabase.Upsert(new SongRecord
                    {
                        Md5 = _currentMd5,
                        Title = SongTitle,
                        Artist = ArtistName
                    });
                }
            }

            AddToPlaylist();

            // 坑：UpdateMetadata会清除缩略图，对非NCM文件，FileInit总是晚于AlbumArtInit，导致设置的缩略图被清空
            _smtcService.UpdateTextMetadata(SongTitle, ArtistName);

            _musicPlayer.SetMasterVolume((float)Volume);

            LoadLyrics();
            if (_pendingSeekTime is { } seekTime)
            {
                _pendingSeekTime = null;
                _musicPlayer.SeekToPosition(seekTime, true);
                ProgressValue = seekTime;
                CurrentTime = FormatTime(seekTime);
                UpdateLyricProgress(seekTime);
            }
            if (_enableAutoPlay)
                _musicPlayer.Start();
        }, null);
    }

    private void OnAlbumArtInit(System.Drawing.Image? image)
    {
        _syncContext.Post(_ =>
        {
            try
            {
                var cached = _currentMd5 is not null ? _songDatabase.FindByMd5(_currentMd5) : null;

                if (cached?.AlbumArt is { Length: > 0 })
                {
                    // 优先使用数据库缓存的专辑图片，丢弃解码得到的图片
                    AlbumCoverImage = LoadBitmapImageFromBytes(cached.AlbumArt);
                }
                else
                {
                    AlbumCoverImage = image != null ? ConvertDrawingImageToWpfImage(image) : null;

                    // 将解码得到的图片写入数据库缓存
                    if (cached is not null)
                    {
                        if (image is not null)
                        {
                            using var artStream = new MemoryStream();
                            image.Save(artStream, System.Drawing.Imaging.ImageFormat.Png);
                            cached.AlbumArt = artStream.ToArray();
                        }
                        else
                        {
                            cached.AlbumArt = null;
                        }
                        _songDatabase.Upsert(cached);
                    }
                }

                var playlistItem = PlaylistItems.FirstOrDefault(p => p.FilePath == _currentFilePath);
                playlistItem?.AlbumCover = AlbumCoverImage;

                Stream? stream = null;
                if (AlbumCoverImage is not null && cached?.AlbumArt is { Length: > 0 })
                {
                    stream = new MemoryStream(cached.AlbumArt);
                }
                else if (image is not null)
                {
                    stream = new MemoryStream();
                    image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Position = 0;
                }
                _smtcService.UpdateMetadata(SongTitle, ArtistName, stream);
                stream?.Dispose();
            }
            finally
            {
                image?.Dispose();
            }
        }, null);
    }

    private void OnTimeChanged(float time)
    {
        _syncContext.Post(_ =>
        {
            if (IsDraggingSlider) return;

            ProgressValue = time;
            CurrentTime = FormatTime(time);
            UpdateLyricProgress(time);
        }, null);
    }

    private void UpdateLyricProgress(float time)
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

    private void OnStart()
    {
        _previousLatencyMode = GCSettings.LatencyMode;
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        if (_currentMd5 is not null)
        {
            _songDatabase.IncrementPlayCount(_currentMd5);
        }
        _syncContext.Post(_ =>
        {
            var playingItem = PlaylistItems.FirstOrDefault(p => p.FilePath == _currentFilePath);
            if (playingItem is not null)
                playingItem.PlayedCount++;
            PlayPauseContent = "\u23F8";
            _smtcService.UpdatePlaybackStatus(PlaybackState.Playing);
            _enableAutoPlay = false;
        }, null);
    }

    private void OnPause()
    {
        GCSettings.LatencyMode = _previousLatencyMode;
        _syncContext.Post(_ =>
        {
            PlayPauseContent = "\u25B6";
            _smtcService.UpdatePlaybackStatus(PlaybackState.Paused);
            UpdateLyricProgress((float)ProgressValue);
        }, null);
    }

    private void OnStop()
    {
        GCSettings.LatencyMode = _previousLatencyMode;
        _syncContext.Post(_ =>
        {
            PlayPauseContent = "\u25B6";
            ProgressValue = 0;
            CurrentTime = "0:00";
            _smtcService.UpdatePlaybackStatus(PlaybackState.Stopped);
            
            if (CurrentLyricIndex >= 0 && CurrentLyricIndex < Lyrics.Count)
            {
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
            if (!_disableAutoAdvance)
                AutoAdvance();
            _disableAutoAdvance = false;
        }, null);
    }

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

    [RelayCommand]
    private void PlayModeToggle()
    {
        CurrentPlayMode = CurrentPlayMode switch
        {
            PlayMode.Sequential => PlayMode.ListLoop,
            PlayMode.ListLoop => PlayMode.SingleLoop,
            PlayMode.SingleLoop => PlayMode.Shuffle,
            _ => PlayMode.Sequential
        };
        _shuffleHistory.Clear();
    }

    [RelayCommand]
    private void PrevSong()
    {
        if (PlaylistItems.Count == 0)
        {
            StopPlayback();
            return;
        }

        if (CurrentPlayMode == PlayMode.Shuffle)
        {
            if (_shuffleHistory.Count > 0)
            {
                var prevPath = _shuffleHistory.Pop();
                PlaySongByPath(prevPath);
            }
            else
            {
                StopPlayback();
            }
            return;
        }

        var currentIndex = GetCurrentPlaylistIndex();
        if (currentIndex < 0)
        {
            StopPlayback();
            return;
        }

        if (CurrentPlayMode == PlayMode.SingleLoop)
        {
            PlaySongByPath(PlaylistItems[currentIndex].FilePath);
            return;
        }

        var prevIndex = currentIndex - 1;
        if (prevIndex < 0)
        {
            if (CurrentPlayMode == PlayMode.ListLoop)
            {
                prevIndex = PlaylistItems.Count - 1;
            }
            else
            {
                StopPlayback();
                return;
            }
        }

        PlaySongByPath(PlaylistItems[prevIndex].FilePath);
    }

    [RelayCommand]
    private void NextSong()
    {
        if (PlaylistItems.Count == 0)
        {
            StopPlayback();
            return;
        }

        if (CurrentPlayMode == PlayMode.Shuffle)
        {
            if (_currentFilePath != null)
                _shuffleHistory.Push(_currentFilePath);

            if (PlaylistItems.Count == 1)
            {
                PlaySongByPath(PlaylistItems[0].FilePath);
                return;
            }

            int nextIndex;
            var currentIndex = GetCurrentPlaylistIndex();
            do
            {
                nextIndex = _shuffleRandom.Next(PlaylistItems.Count);
            } while (nextIndex == currentIndex && PlaylistItems.Count > 1);

            PlaySongByPath(PlaylistItems[nextIndex].FilePath);
            return;
        }

        var curIndex = GetCurrentPlaylistIndex();
        if (curIndex < 0)
        {
            StopPlayback();
            return;
        }

        if (CurrentPlayMode == PlayMode.SingleLoop)
        {
            PlaySongByPath(PlaylistItems[curIndex].FilePath);
            return;
        }

        var nextIdx = curIndex + 1;
        if (nextIdx >= PlaylistItems.Count)
        {
            if (CurrentPlayMode == PlayMode.ListLoop)
            {
                nextIdx = 0;
            }
            else
            {
                // 顺序播放在下一曲不可用时停止
                StopPlayback();
                return;
            }
        }

        PlaySongByPath(PlaylistItems[nextIdx].FilePath);
    }

    private void AutoAdvance()
    {
        if (PlaylistItems.Count == 0) return;

        switch (CurrentPlayMode)
        {
            case PlayMode.SingleLoop:
                if (_currentFilePath != null)
                    PlaySongByPath(_currentFilePath);
                break;
            case PlayMode.Sequential:
            case PlayMode.ListLoop:
            case PlayMode.Shuffle:
                NextSong();
                break;
        }
    }

    private int GetCurrentPlaylistIndex()
    {
        if (_currentFilePath == null) return -1;
        for (var i = 0; i < PlaylistItems.Count; i++)
        {
            if (PlaylistItems[i].FilePath == _currentFilePath)
                return i;
        }
        return -1;
    }

    private void PlaySongByPath(string filePath)
    {
        _enableAutoPlay = true;
        try
        {
            Task.Run(() => OpenFile(filePath));
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"{ex.Message}\n{filePath}", "Error", WpfMessageBoxIcon.Error);
        }
    }

    // 手动触发停止播放，不进行AutoAdvance
    private void StopPlayback()
    {
        _disableAutoAdvance = true;
        if (_musicPlayer.IsInitialized() && _musicPlayer.IsPlaying())
        {
            _musicPlayer.Stop();
        }
        else
        {
            _disableAutoAdvance = false;
        }
    }

    [RelayCommand]
    private async Task PlayPause()
    {
        if (!_musicPlayer.IsInitialized())
        {
            _syncContext.Post(_ => PlayPauseContent = "\u25B6", null);
            return;
        }

        if (_musicPlayer.IsPlaying())
        {
            _musicPlayer.Pause();
        }
        else
        {
            IsDraggingSlider = true;
            _musicPlayer.Start();
            await Task.Delay(200);
            IsDraggingSlider = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (IsFileDialogOpen == true) { return; }
        IsFileDialogOpen = true;
        var path = await _fileDialogService.PickMusicFileAsync();

        IsFileDialogOpen = false;
        if (path == null) return;

        try
        {
            await Task.Run(() => OpenFile(path));
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"{ex.Message}\n{path}", "Error",
                WpfMessageBoxIcon.Error);
            // reset state
            AlbumCoverImage = null;
            SongTitle = "Unknown Title";
            ArtistName = "Unknown Artist";
            ProgressValue = 0;
            CurrentTime = "0:00";
            _smtcService.UpdatePlaybackStatus(PlaybackState.Stopped);
            _smtcService.UpdateTextMetadata("Unknown Title", "Unknown Artist");
            _lrcFileController?.Dispose();
            _lrcFileController = null;
            Lyrics.Clear();
            CurrentLyricIndex = -1;
            HasTranslationAvailable = false;
            HasRomanjiAvailable = false;
        }
    }

    [RelayCommand]
    private async Task OpenPlaylistAsync()
    {
        if (HasUnsavedPlaylistChanges)
        {
            var result = WpfMessageBox.Show(
                "播放列表有未保存的更改，是否保存？",
                "确认",
                WpfMessageBoxButton.YesNoCancel,
                WpfMessageBoxIcon.Question);

            switch (result)
            {
                case WpfMessageBoxResult.Yes:
                    await SavePlaylistAsync();
                    break;
                case WpfMessageBoxResult.Cancel:
                    return;
            }
        }
        if (IsFileDialogOpen) { return; }
        IsFileDialogOpen = true;
        var path = await _fileDialogService.PickJsonAsync();

        IsFileDialogOpen = false;

        if (path == null) return;
        try
        {
            var errorCode = _playlistProvider.Load(path);
            if (errorCode != IPlaylistProvider.ErrorCode.NoError)
            {
                WpfMessageBox.Show($"加载播放列表失败: {errorCode}", "Error", WpfMessageBoxIcon.Error);
                return;
            }

            var playlist = _playlistProvider.GetPlaylist();

            if (string.IsNullOrEmpty(playlist.Id))
            {
                WpfMessageBox.Show("播放列表格式错误: 缺少 ID", "Error", WpfMessageBoxIcon.Error);
                return;
            }

            if (playlist.FormatVersion != 3)
            {
                WpfMessageBox.Show($"播放列表格式版本不支持: {playlist.FormatVersion}，需要版本 3", "Error", WpfMessageBoxIcon.Error);
                return;
            }

            PlaylistItems.Clear();

            // 加载播放列表标题
            PlaylistTitle = string.IsNullOrEmpty(playlist.Name) ? "播放列表" : playlist.Name;

            // 加载播放列表封面（所有类型均尝试加载，失败时静默回落至默认封面）
            PlaylistCoverImage = TryLoadPlaylistCover(playlist.Cover);

            foreach (var content in playlist.Contents)
            {
                if (!File.Exists(content.File)) continue;

                var cached = _songDatabase.FindByMd5(content.Md5);
                var title = cached?.Title ?? Path.GetFileNameWithoutExtension(content.File);
                var artist = cached?.Artist ?? "Unknown Artist";
                var playedCount = cached?.PlayCount ?? 0;

                var itemVM = new PlaylistItemViewModel(content.File, title, artist, playedCount);
                if (cached?.AlbumArt is { Length: > 0 })
                    itemVM.AlbumCover = LoadBitmapImageFromBytes(cached.AlbumArt);

                PlaylistItems.Add(itemVM);
            }

            if (PlaylistItems.Count > 0)
            {
                var first = PlaylistItems[0];
                _enableAutoPlay = IsMusicPlaying;

                await Task.Run(() => OpenFile(first.FilePath));
            }

            _isPlaylistUserOpened = true;
            _isPlaylistDirty = false;
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"加载播放列表失败: {ex.Message}\n{path}", "Error", WpfMessageBoxIcon.Error);
        }
    }

    [RelayCommand]
    public async Task SavePlaylistAsync()
    {
        var path = _playlistProvider.CurrentFilePath;

        if (string.IsNullOrEmpty(path))
        {
            if (IsFileDialogOpen) { return; }
            IsFileDialogOpen = true;
            path = await _fileDialogService.SaveJsonAsync();
            IsFileDialogOpen = false;
            if (path == null) return;
        }

        try
        {
            var playlist = _playlistProvider.GetPlaylist();

            if (string.IsNullOrEmpty(playlist.Id))
                playlist.Id = Guid.NewGuid().ToString();

            playlist.FormatVersion = 3;
            playlist.CreatedAt = DateTimeOffset.Now;
            playlist.Name = PlaylistTitle;
            playlist.Contents.Clear();

            foreach (var item in PlaylistItems)
            {
                var md5 = ComputeFileMd5(item.FilePath);
                playlist.Contents.Add(new ContentRecord
                {
                    File = item.FilePath,
                    Md5 = md5
                });
            }

            var errorCode = _playlistProvider.Save(path);
            if (errorCode != IPlaylistProvider.ErrorCode.NoError)
            {
                WpfMessageBox.Show($"保存播放列表失败: {errorCode}", "Error", WpfMessageBoxIcon.Error);
            }
            else
            {
                _isPlaylistDirty = false;
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"保存播放列表失败: {ex.Message}\n{path}", "Error", WpfMessageBoxIcon.Error);
        }
    }

    [RelayCommand]
    private async Task AddSongToPlaylistAsync()
    {
        if (IsFileDialogOpen) return;
        IsFileDialogOpen = true;
        var paths = await _fileDialogService.PickMusicFilesAsync();
        IsFileDialogOpen = false;

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            if (PlaylistItems.Any(p => p.FilePath == path)) continue;

            var md5 = ComputeFileMd5(path);
            var cached = _songDatabase.FindByMd5(md5);
            var title = cached?.Title ?? Path.GetFileNameWithoutExtension(path);
            var artist = cached?.Artist ?? "Unknown Artist";
            var playedCount = cached?.PlayCount ?? 0;

            var itemVM = new PlaylistItemViewModel(path, title, artist, playedCount);
            if (cached?.AlbumArt is { Length: > 0 })
                itemVM.AlbumCover = LoadBitmapImageFromBytes(cached.AlbumArt);

            PlaylistItems.Add(itemVM);
            _isPlaylistUserOpened = true;
            _isPlaylistDirty = true;
        }
    }

    [RelayCommand]
    private void RemoveSongFromPlaylist(PlaylistItemViewModel? item)
    {
        if (item == null) return;
        PlaylistItems.Remove(item);
        _isPlaylistUserOpened = true;
        _isPlaylistDirty = true;
    }

    [RelayCommand]
    private async Task ChangePlaylistCoverAsync()
    {
        if (IsFileDialogOpen) return;
        IsFileDialogOpen = true;
        var path = await _fileDialogService.PickImageAsync();
        IsFileDialogOpen = false;

        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(path);

            // 压缩图片为JPEG并限制尺寸
            var compressedBytes = CompressImageToJpeg(imageBytes, 300, 300);

            var playlist = _playlistProvider.GetPlaylist();
            playlist.Cover = new CoverRecord
            {
                Type = CoverType.Base64,
                Data = compressedBytes,
                Url = null
            };

            PlaylistCoverImage = LoadBitmapImageFromBytes(compressedBytes);
            _isPlaylistUserOpened = true;
            _isPlaylistDirty = true;
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"设置封面失败: {ex.Message}", "Error", WpfMessageBoxIcon.Error);
        }
    }

    private static byte[] CompressImageToJpeg(byte[] imageData, int maxWidth, int maxHeight)
    {
        using var inputStream = new MemoryStream(imageData);
        var decoder = BitmapDecoder.Create(inputStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        var scaleX = (double)maxWidth / frame.PixelWidth;
        var scaleY = (double)maxHeight / frame.PixelHeight;
        var scale = Math.Min(scaleX, scaleY);
        if (scale > 1) scale = 1; // 不放大

        var transformed = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));

        var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
        encoder.Frames.Add(BitmapFrame.Create(transformed));

        using var outputStream = new MemoryStream();
        encoder.Save(outputStream);
        return outputStream.ToArray();
    }

    private void LoadLyrics()
    {
        // FFmpeg会自动转换ID3v2 tag到UTF-8
        var lyricsStr = _musicPlayer.GetID3Lyric();
        Lyrics.Clear();
        CurrentLyricIndex = -1;
        HasTranslationAvailable = false;
        HasRomanjiAvailable = false;

        if (!string.IsNullOrEmpty(lyricsStr))
        {
            try
            {
                ParseAndAddLocalLyric(lyricsStr);
                return;
            }
            catch
            {
                // ignored
                // fallback to lrc file read
            }
        }

        // 文件的编码可能是乱来的，不可信
        var lrcPath = FindBestLrcFile();
        if (!string.IsNullOrEmpty(lrcPath))
        {
            try
            {
                var content_bytes = File.ReadAllBytes(lrcPath);
                var content = LocaleConverter.GetSystemStringFromBytes(content_bytes);
                ParseAndAddLocalLyric(content);
                return;
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Failed to load lrc: {ex.Message}", "Error", WpfMessageBoxIcon.Error);
            }
        }

        // Fallback to same filename
        var exactLrcPath = Path.ChangeExtension(_currentFilePath, ".lrc");
        if (File.Exists(exactLrcPath))
        {
            try
            {
                var content_bytes = File.ReadAllBytes(exactLrcPath);
                var content = LocaleConverter.GetSystemStringFromBytes(content_bytes);
                ParseAndAddLocalLyric(content);
                return;
            }
            catch (InvalidOperationException ex)
            {
                // ignored
                WpfMessageBox.Show(ex.Message, "Error", WpfMessageBoxIcon.Error);
            }
        }

        _lrcFileController = null;
        Lyrics.Add(new LyricLineViewModel("暂无歌词"));
    }

    private void ParseAndAddLocalLyric(string content)
    {
        _lrcFileController?.Dispose();
        _lrcFileController = new LrcFileController();

        _lrcFileController.ParseLrcStream(content);
        _lrcFileController.SetSongDuration(_musicPlayer.GetMusicTimeLength());
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

    private string? FindBestLrcFile()
    {
        if (string.IsNullOrEmpty(_currentFilePath)) return null;

        var fileDir = Path.GetDirectoryName(_currentFilePath);
        if (string.IsNullOrEmpty(fileDir)) return null;

        var searchPaths = new List<string>
        {
            fileDir,
            Path.GetFullPath(Path.Combine(fileDir, "..")),
        };

        AddKnownPath(Environment.SpecialFolder.MyMusic);
        AddKnownPath(Environment.SpecialFolder.MyDocuments);

        var targetName = _musicPlayer.GetSongTitle();
        if (string.IsNullOrEmpty(targetName))
        {
            targetName = Path.GetFileNameWithoutExtension(_currentFilePath);
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
            catch { }
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
    private void TogglePlaylist()
    {
        ActiveView = ActiveView == ActiveView.Playlist ? ActiveView.Player : ActiveView.Playlist;
    }

    private void AddToPlaylist()
    {
        if (_currentFilePath == null
            || _currentMd5 == null) return;
        if (PlaylistItems.All(p => p.FilePath != _currentFilePath))
        {
            // query database, get album cover
            var cached = _songDatabase.FindByMd5(_currentMd5);
            var itemVM = new PlaylistItemViewModel(
                _currentFilePath, SongTitle, ArtistName, cached?.PlayCount ?? 0);
            if (cached?.AlbumArt is { Length: > 0 })
                itemVM.AlbumCover = LoadBitmapImageFromBytes(cached.AlbumArt);
            else if (AlbumCoverImage is not null)
                itemVM.AlbumCover = AlbumCoverImage;
            PlaylistItems.Add(itemVM);
            if (_isPlaylistUserOpened)
                // 若用户打开了播放列表，此操作会造成插入歌曲到播放列表中
                _isPlaylistDirty = true;
        }
        foreach (var item in PlaylistItems)
            item.IsPlaying = item.FilePath == _currentFilePath;
    }

    public async void PlayFromPlaylist(PlaylistItemViewModel item)
    {
        // IsPlaylistVisible = false;
        try
        {
            _enableAutoPlay = true;
            // 避免ui冻结
            await Task.Run(() => OpenFile(item.FilePath));
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"{ex.Message}\n{item.FilePath}", "Error", WpfMessageBoxIcon.Error);
        }
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

    private static BitmapImage ConvertDrawingImageToWpfImage(System.Drawing.Image drawingImage)
    {
        using var memoryStream = new MemoryStream();
        drawingImage.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
        memoryStream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = memoryStream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }

    private static BitmapImage LoadBitmapImageFromBytes(byte[] data)
    {
        using var memoryStream = new MemoryStream(data);
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = memoryStream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }

    private static BitmapImage? TryLoadPlaylistCover(CoverRecord cover)
    {
        try
        {
            switch (cover.Type)
            {
                case CoverType.Base64 when cover.Data is { Length: > 0 }:
                    return LoadBitmapImageFromBytes(cover.Data);

                case CoverType.Local when !string.IsNullOrEmpty(cover.Url):
                {
                    if (!File.Exists(cover.Url)) return null;
                    var bytes = File.ReadAllBytes(cover.Url);
                    return LoadBitmapImageFromBytes(bytes);
                }

                case CoverType.Cloud when !string.IsNullOrEmpty(cover.Url):
                {
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.UriSource = new Uri(cover.Url, UriKind.Absolute);
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    return bitmapImage;
                }

                default:
                    return null;
            }
        }
        catch
        {
            // 加载失败时静默回落至默认封面
            return null;
        }
    }

    private static string ComputeFileMd5(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = MD5.HashData(stream);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static string FormatTime(float seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        return $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
    }
}
