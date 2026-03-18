using MusicPlayerLibrary;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WpfMusicPlayer.Services;

namespace WpfMusicPlayer.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IFileDialogService _fileDialogService;
    private readonly SynchronizationContext _syncContext;
    private MusicPlayer _musicPlayer;

    public MainViewModel(IFileDialogService fileDialogService)
    {
        _fileDialogService = fileDialogService;
        _syncContext = SynchronizationContext.Current!;
        _musicPlayer = new MusicPlayer();
        SubscribePlayerEvents();

        PlayPauseCommand = new RelayCommand(OnPlayPause);
        OpenCommand = new RelayCommand(async () => await OnOpenAsync());
        PrevCommand = new RelayCommand(() => { });
        NextCommand = new RelayCommand(() => { });
    }

    public string SongTitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Song Title";

    public string ArtistName
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Artist Name";

    public BitmapImage? AlbumCoverImage
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string CurrentTime
    {
        get;
        private set => SetProperty(ref field, value);
    } = "0:00";

    public string TotalTime
    {
        get;
        private set => SetProperty(ref field, value);
    } = "3:30";

    public double ProgressValue
    {
        get;
        set => SetProperty(ref field, value);
    }

    public double ProgressMaximum
    {
        get;
        private set => SetProperty(ref field, value);
    } = 100;

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

    public string PlayPauseContent
    {
        get;
        private set => SetProperty(ref field, value);
    } = "\u25B6";

    public ObservableCollection<string> Lyrics { get; } = [];

    public bool IsDraggingSlider { get; set; }

    public ICommand PlayPauseCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand NextCommand { get; }

    public void OpenFile(string filePath)
    {
        _musicPlayer.Dispose();
        _musicPlayer = new MusicPlayer();
        SubscribePlayerEvents();
        _musicPlayer.OpenFile(filePath);
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
    }

    public void OnWindowClosed()
    {
        if (_musicPlayer.IsInitialized())
        {
            _musicPlayer.Stop();
        }
    }

    public void Dispose()
    {
        _musicPlayer.Dispose();
        GC.SuppressFinalize(this);
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

    private void OnFileInit()
    {
        _syncContext.Post(_ =>
        {
            var length = _musicPlayer.GetMusicTimeLength();
            ProgressMaximum = length;
            TotalTime = FormatTime(length);

            SongTitle = _musicPlayer.GetSongTitle() ?? "Unknown Title";
            ArtistName = _musicPlayer.GetSongArtist() ?? "Unknown Artist";

            _musicPlayer.SetMasterVolume((float)Volume);

            LoadLyrics();
        }, null);
    }

    private void OnAlbumArtInit(System.Drawing.Image? image)
    {
        _syncContext.Post(_ =>
        {
            AlbumCoverImage = image != null ? ConvertDrawingImageToWpfImage(image) : null;
        }, null);
    }

    private void OnTimeChanged(float time)
    {
        _syncContext.Post(_ =>
        {
            if (IsDraggingSlider) return;
            
            ProgressValue = time;
            CurrentTime = FormatTime(time);
        }, null);
    }

    private void OnStart()
    {
        _syncContext.Post(_ => PlayPauseContent = "\u23F8", null);
    }

    private void OnPause()
    {
        _syncContext.Post(_ => PlayPauseContent = "\u25B6", null);
    }

    private void OnStop()
    {
        _syncContext.Post(_ =>
        {
            PlayPauseContent = "\u25B6";
            ProgressValue = 0;
            CurrentTime = "0:00";
        }, null);
    }

    private void OnPlayPause()
    {
        if (!_musicPlayer.IsInitialized()) return;

        if (_musicPlayer.IsPlaying())
        {
            _musicPlayer.Pause();
        }
        else
        {
            _musicPlayer.Start();
        }
    }

    private async Task OnOpenAsync()
    {
        var path = await _fileDialogService.PickMusicFileAsync();
        if (path == null) return;

        try
        {
            OpenFile(path);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show($"{ex.Message}\n{path}", "Error");
        }
    }

    private void LoadLyrics()
    {
        var lyricsStr = _musicPlayer.GetID3Lyric();
        Lyrics.Clear();

        if (string.IsNullOrEmpty(lyricsStr))
        {
            Lyrics.Add("No lyrics available");
            return;
        }

        var lines = lyricsStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            Lyrics.Add(line);
        }
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

    private static string FormatTime(float seconds)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
        return $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
    }
}
