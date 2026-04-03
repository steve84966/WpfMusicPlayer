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
using Microsoft.Extensions.Logging;

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
    private bool _disableAutoAdvance;
    private bool _isDisposed;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        IConfigProvider configProvider,
        IFileDialogService fileDialogService,
        ISmtcService smtcService,
        ISongDatabaseService songDatabase,
        ICommandLineParser commandLineParser,
        PlaylistViewModel playlist,
        ILogger<MainViewModel> logger)
    {
        _configProvider = configProvider;
        _fileDialogService = fileDialogService;
        _smtcService = smtcService;
        _songDatabase = songDatabase;
        _commandLineParser = commandLineParser;
        _logger = logger;
        _syncContext = SynchronizationContext.Current!;
        Equalizer = new EqualizerViewModel(ApplyEqualizerBand);
        Settings = new SettingsViewModel(configProvider);
        Settings.SettingChanged += OnSettingChanged;
        Playlist = playlist;
        Playlist.IsMusicPlayingQuery = () => _musicPlayer!.IsPlaying();
        Playlist.PlaySongRequested += OnPlaylistSongRequested;
        Playlist.ResetPlaylistRequested += OnPlaylistResetRequested;
        CurrentBackgroundMode = configProvider.GetConfig().UI.Background;
        _sampleRate = 48000; // Studio quality
        _musicPlayer = new MusicPlayer(_sampleRate);

        SubscribePlayerEvents();
        SubscribeSmtcEvents();
        RestoreSettingsFromCommandLine();
        _logger.LogInformation("MainViewModel initialized, sample rate: {SampleRate}", _sampleRate);
    }

    private void OnPlaylistSongRequested(string filePath, bool autoPlay)
    {
        _enableAutoPlay = autoPlay;
        try
        {
            Task.Run(() => OpenFile(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError("PlaySongRequested exception: {Message}", ex.Message);
            WpfMessageBox.Show($"{ex.Message}\n{filePath}", "Error", WpfMessageBoxIcon.Error);
        }
    }

    private void OnPlaylistResetRequested()
    {
        // reset state
        _musicPlayer.Dispose();
        _musicPlayer = new MusicPlayer();
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

    private void RestoreSettingsFromCommandLine()
    {
        _logger.LogInformation("Restoring settings from command line");
        Volume = _commandLineParser.Volume;

        if (string.IsNullOrEmpty(_commandLineParser.FilePath))
        {
            _logger.LogInformation("No file path from command line, using startup view: {View}", _commandLineParser.StartupView);
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

        _logger.LogInformation("Restoring file from command line: {FilePath}, autoPlay: {AutoPlay}, seekTime: {SeekTime}",
            _commandLineParser.FilePath, _enableAutoPlay, _pendingSeekTime);
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

    public PlaylistViewModel Playlist { get; }

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
        _logger.LogInformation("Attempting to acquire OpenFile lock");
        lock (_openFileLock)
        {
            _logger.LogInformation("OpenFile lock acquired, filePath: {FilePath}", filePath);
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
                _logger.LogInformation("NCM file detected, starting decode: {FilePath}", filePath);
                _syncContext.Send(_ => IsDecoding = true, null);
            }
            try
            {
                _currentFilePath = filePath;
                _currentMd5 = ComputeFileMd5(filePath);
                _logger.LogInformation("File MD5 computed: {Md5}", _currentMd5);
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
        _logger.LogInformation("SeekToCurrentPosition: targetTime={TargetTime}s, isPlaying={IsPlaying}", targetTime, isPlaying);

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
        _logger.LogInformation("SeekToLyric: timeMs={TimeMs}, targetTimeSec={TargetTime}s", lyric.TimeMs, targetTimeSec);

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
        _logger.LogInformation("Window closed, stopping music player");
        if (_musicPlayer.IsInitialized())
        {
            _musicPlayer.Stop();
        }
    }

    public bool HasUnsavedPlaylistChanges => Playlist.HasUnsavedPlaylistChanges;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        _logger.LogInformation("Setting changed: {SettingName}", e.SettingName);
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
        if (!_isDisposed)
        {
            _logger.LogInformation("MainViewModel disposing, releasing resources");
            Settings.SettingChanged -= OnSettingChanged;
            Playlist.PlaySongRequested -= OnPlaylistSongRequested;
            Playlist.ResetPlaylistRequested += OnPlaylistResetRequested;
            GCSettings.LatencyMode = _previousLatencyMode;
            _musicPlayer.Dispose();
            _logger.LogInformation("MusicPlayer disposed");
            _songDatabase.Dispose();
            _logger.LogInformation("SongDatabase disposed");
            GC.SuppressFinalize(this);
        }

        _isDisposed = true;
    }

    public async Task SetSampleRate(int sampleRate)
    {
        if (sampleRate < 8000 // Telephone quality
            || sampleRate > 192000) // Above high-resolution audio
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be between 8000 and 192000 Hz.");
        }
        _logger.LogInformation("Sample rate changed: {OldRate} -> {NewRate}", _sampleRate, sampleRate);
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
            _logger.LogInformation("OnFileInit: file initialized, loading metadata");
            IsDecoding = false;
            var length = _musicPlayer.GetMusicTimeLength();
            ProgressMaximum = length;
            TotalTime = FormatTime(length);

            var cached = _currentMd5 is not null ? _songDatabase.FindByMd5(_currentMd5) : null;
            if (cached is not null)
            {
                // 优先使用数据库缓存
                _logger.LogInformation("OnFileInit: using cached metadata for {Md5}, title: {Title}", _currentMd5, cached.Title);
                SongTitle = cached.Title;
                ArtistName = cached.Artist;
            }
            else
            {
                _logger.LogInformation("OnFileInit: no cache found, reading metadata from file");
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
                _logger.LogInformation("OnFileInit: applying pending seek to {SeekTime}s", seekTime);
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
                    _logger.LogInformation("Cached MD5 hit, discarding decoded image");
                }
                else
                {
                    _logger.LogInformation("MD5 miss, loading image from OnAlbumArtInit");
                    AlbumCoverImage = image != null ? ConvertDrawingImageToWpfImage(image) : null;

                    // 将解码得到的图片写入数据库缓存
                    if (cached is not null)
                    {
                        if (image is not null)
                        {
                            _logger.LogInformation("Save image as PNG raw data");
                            using var artStream = new MemoryStream();
                            image.Save(artStream, System.Drawing.Imaging.ImageFormat.Png);
                            cached.AlbumArt = artStream.ToArray();
                        }
                        else
                        {
                            _logger.LogInformation("Image is null, exiting");
                            cached.AlbumArt = null;
                        }
                        _songDatabase.Upsert(cached);
                    }
                }

                var playlistItem = Playlist.PlaylistItems.FirstOrDefault(p => p.FilePath == _currentFilePath);
                if (playlistItem is not null)
                    playlistItem.AlbumCover = AlbumCoverImage;

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
                    _logger.LogInformation("Update PNG stream for SMTC controller read");
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
        _logger.LogInformation("OnStart: playback started, switching to SustainedLowLatency GC mode");
        _previousLatencyMode = GCSettings.LatencyMode;
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        if (_currentMd5 is not null)
        {
            _songDatabase.IncrementPlayCount(_currentMd5);
        }
        _syncContext.Post(_ =>
        {
            Playlist.IncrementItemPlayedCount(_currentFilePath);
            PlayPauseContent = "\u23F8";
            _smtcService.UpdatePlaybackStatus(PlaybackState.Playing);
            _enableAutoPlay = false;
        }, null);
    }

    private void OnPause()
    {
        _logger.LogInformation("OnPause: playback paused, restoring GC mode");
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
        _logger.LogInformation("OnStop: playback stopped, restoring GC mode");
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
            if (!_disableAutoAdvance && !_isDisposed)
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
        _logger.LogInformation("Play mode changed to {PlayMode}", CurrentPlayMode);
        _shuffleHistory.Clear();
    }

    [RelayCommand]
    private void PrevSong()
    {
        var items = Playlist.PlaylistItems;
        if (items.Count == 0)
        {
            _logger.LogInformation("No songs available in playlist, stopping playback");
            StopPlayback();
            return;
        }

        var currentIndex = Playlist.GetIndexByPath(_currentFilePath);
        if (CurrentPlayMode == PlayMode.Shuffle)
        {
            if (_shuffleHistory.Count > 0)
            {
                _logger.LogInformation("Popping previous shuffled track from stack");
                var prevPath = _shuffleHistory.Pop();
                PlaySongByPath(prevPath);
            }
            else
            {
                int nextIndex;
                do
                {
                    nextIndex = _shuffleRandom.Next(items.Count);
                } while (nextIndex == currentIndex && items.Count > 1);
            }
            return;
        }

        if (currentIndex < 0)
        {
            _logger.LogInformation("Invalid current index, stopping playback");
            StopPlayback();
            return;
        }

        if (CurrentPlayMode == PlayMode.SingleLoop)
        {
            _logger.LogInformation("Single Loop triggered, playing current song");
            PlaySongByPath(items[currentIndex].FilePath);
            return;
        }

        var prevIndex = currentIndex - 1;
        if (prevIndex < 0)
        {
            if (CurrentPlayMode == PlayMode.ListLoop)
            {
                _logger.LogInformation("List loop triggered, playing song in the end of playlist");
                prevIndex = items.Count - 1;
            }
            else
            {
                StopPlayback();
                return;
            }
        }

        PlaySongByPath(items[prevIndex].FilePath);
    }

    [RelayCommand]
    private void NextSong()
    {
        var items = Playlist.PlaylistItems;
        if (items.Count == 0)
        {
            _logger.LogInformation("No songs available in playlist, stopping playback");
            StopPlayback();
            return;
        }

        if (CurrentPlayMode == PlayMode.Shuffle)
        {
            if (_currentFilePath != null)
            {
                _logger.LogInformation("Push current index to shuffle stack");
                _shuffleHistory.Push(_currentFilePath);
            }

            if (items.Count == 1)
            {
                _logger.LogInformation("Shuffle enabled and only 1 file in playlist, repeating");
                PlaySongByPath(items[0].FilePath);
                return;
            }

            int nextIndex;
            var currentIndex = Playlist.GetIndexByPath(_currentFilePath);
            do
            {
                _logger.LogInformation("Attempting to generate new playlist index");
                nextIndex = _shuffleRandom.Next(items.Count);
            } while (nextIndex == currentIndex && items.Count > 1);

            PlaySongByPath(items[nextIndex].FilePath);
            return;
        }

        var curIndex = Playlist.GetIndexByPath(_currentFilePath);
        if (curIndex < 0)
        {
            StopPlayback();
            return;
        }

        if (CurrentPlayMode == PlayMode.SingleLoop)
        {
            _logger.LogInformation("Single Loop triggered, playing current song");
            PlaySongByPath(items[curIndex].FilePath);
            return;
        }

        var nextIdx = curIndex + 1;
        if (nextIdx >= items.Count)
        {
            if (CurrentPlayMode == PlayMode.ListLoop)
            {
                _logger.LogInformation("List loop triggered, playing song in the start of playlist");
                nextIdx = 0;
            }
            else
            {
                // 顺序播放在下一曲不可用时停止
                _logger.LogInformation("No songs available in playlist, stop!");
                StopPlayback();
                return;
            }
        }

        PlaySongByPath(items[nextIdx].FilePath);
    }

    private void AutoAdvance()
    {
        if (Playlist.PlaylistItems.Count == 0) return;
        _logger.LogInformation("AutoAdvance triggered, current play mode: {PlayMode}", CurrentPlayMode);

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
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void PlaySongByPath(string filePath)
    {
        _logger.LogInformation("PlaySongByPath: {FilePath}", filePath);
        _enableAutoPlay = true;
        try
        {
            Task.Run(() => OpenFile(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception thrown: {Message}", ex.Message);
            WpfMessageBox.Show($"{ex.Message}\n{filePath}", "Error", WpfMessageBoxIcon.Error);
        }
    }

    // 手动触发停止播放，不进行AutoAdvance
    private void StopPlayback()
    {
        _logger.LogInformation("StopPlayback: manual stop requested, disabling auto-advance");
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
            _logger.LogInformation("PlayPause: player not initialized, ignoring");
            _syncContext.Post(_ => PlayPauseContent = "\u25B6", null);
            return;
        }

        if (_musicPlayer.IsPlaying())
        {
            _logger.LogInformation("PlayPause: pausing playback");
            _musicPlayer.Pause();
        }
        else
        {
            _logger.LogInformation("PlayPause: resuming playback");
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
        _logger.LogInformation("OpenAsync: opening file dialog");
        var path = await _fileDialogService.PickMusicFileAsync();

        IsFileDialogOpen = false;
        if (path == null)
        {
            _logger.LogInformation("OpenAsync: user cancelled file dialog");
            return;
        }

        _logger.LogInformation("OpenAsync: user selected file {FilePath}", path);
        try
        {
            await Task.Run(() => OpenFile(path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAsync: failed to open file {FilePath}", path);
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
    public async Task SavePlaylistAsync()
    {
        await Playlist.SavePlaylistAsync();
    }

    private void LoadLyrics()
    {
        _logger.LogInformation("LoadLyrics: loading lyrics for {FilePath}", _currentFilePath);
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
                _logger.LogInformation("LoadLyrics: found embedded ID3 lyrics");
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
            _logger.LogInformation("LoadLyrics: found best match LRC file: {LrcPath}", lrcPath);
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
            _logger.LogInformation("LoadLyrics: fallback to exact LRC path: {LrcPath}", exactLrcPath);
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
        _logger.LogInformation("LoadLyrics: no lyrics found");
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
        Playlist.AddSong(_currentFilePath, _currentMd5, SongTitle, ArtistName, AlbumCoverImage);
        Playlist.SetPlayingItem(_currentFilePath);
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
