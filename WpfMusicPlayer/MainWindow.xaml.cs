using MusicPlayerLibrary;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WinRT.Interop;

namespace WpfMusicPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MusicPlayer _musicPlayer;
        private bool _isDraggingSlider = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializePlayer();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;

            PlayPauseButton.Click += PlayPauseButton_Click;
            ProgressSlider.PreviewMouseDown += ProgressSlider_PreviewMouseDown;
            ProgressSlider.PreviewMouseUp += ProgressSlider_PreviewMouseUp;
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
            OpenButton.Click += OpenButton_Click;
        }

        private void InitializePlayer()
        {
            _musicPlayer = new MusicPlayer();

            _musicPlayer.OnPlayerTimeChange += OnTimeChanged;
            _musicPlayer.OnPlayerFileInit += OnFileInit;
            _musicPlayer.OnPlayerAlbumArtInit += OnAlbumArtInit;
            _musicPlayer.OnPlayerStart += OnStart;
            _musicPlayer.OnPlayerPause += OnPause;
            _musicPlayer.OnPlayerStop += OnStop;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Allow drag and drop
            AllowDrop = true;
            Drop += MainWindow_Drop;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            if (_musicPlayer.IsInitialized())
            {
                _musicPlayer.Stop();
            }
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    OpenFile(files[0]);
                }
            }
        }

        private void OpenFile(string filePath)
        {
            _musicPlayer.Dispose();
            InitializePlayer();
            _musicPlayer.OpenFile(filePath);
        }

        private void OnFileInit()
        {
            Dispatcher.BeginInvoke(() =>
            {
                float length = _musicPlayer.GetMusicTimeLength();
                ProgressSlider.Maximum = length;
                TotalTimeTextBlock.Text = FormatTime(length);

                SongTitleTextBlock.Text = _musicPlayer.GetSongTitle() ?? "Unknown Title";
                ArtistTextBlock.Text = _musicPlayer.GetSongArtist() ?? "Unknown Artist";

                _musicPlayer.SetMasterVolume((float)VolumeSlider.Value);

                LoadLyrics();
            });
        }

        private void OnAlbumArtInit(System.Drawing.Image image)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (image != null)
                {
                    AlbumCoverImage.Source = ConvertDrawingImageToWpfImage(image);
                }
                else
                {
                    AlbumCoverImage.Source = null;
                }
            });
        }

        private BitmapImage ConvertDrawingImageToWpfImage(System.Drawing.Image drawingImage)
        {
            using (var memoryStream = new MemoryStream())
            {
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
        }

        private void OnTimeChanged(float time)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isDraggingSlider)
                {
                    ProgressSlider.Value = time;
                    CurrentTimeTextBlock.Text = FormatTime(time);
                }
            });
        }

        private void OnStart()
        {
            Dispatcher.BeginInvoke(() =>
            {
                PlayPauseButton.Content = "⏸"; // Pause symbol
            });
        }

        private void OnPause()
        {
            Dispatcher.BeginInvoke(() =>
            {
                PlayPauseButton.Content = "▶"; // Play symbol
            });
        }

        private void OnStop()
        {
            Dispatcher.BeginInvoke(() =>
            {
                PlayPauseButton.Content = "▶";
                ProgressSlider.Value = 0;
                CurrentTimeTextBlock.Text = "0:00";
            });
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
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
            if (file == null) return; // User cancelled
            var path = file.Path;
            try
            {
                OpenFile(path);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show($"{ex.Message}\n{path}", "Error");
            }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
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

        private void ProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void ProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            if (_musicPlayer.IsInitialized())
            {
                _musicPlayer.SeekToPosition((float)ProgressSlider.Value, false);
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_musicPlayer != null)
            {
                _musicPlayer.SetMasterVolume((float)e.NewValue);
            }
        }

        private void LoadLyrics()
        {
            string lyricsStr = _musicPlayer.GetID3Lyric();
            LyricsListBox.Items.Clear();

            if (string.IsNullOrEmpty(lyricsStr))
            {
                LyricsListBox.Items.Add("No lyrics available");
                return;
            }

            // Simple line splitting for now, a real lyrics parser would handle timestamps
            string[] lines = lyricsStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                LyricsListBox.Items.Add(line);
            }
        }

        private string FormatTime(float seconds)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
            return $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
        }
    }
}