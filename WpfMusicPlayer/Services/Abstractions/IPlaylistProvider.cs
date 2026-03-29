using WpfMusicPlayer.Models;

namespace WpfMusicPlayer.Services.Abstractions;

public interface IPlaylistProvider
{
    public enum ErrorCode
    {
        NoError,
        FileNotFound,
        PermissionDenied,
        FileOpenFailed,
        ParseError,
        UnknownError
    }

    ErrorCode Load(string filePath);

    ErrorCode Save(string filePath);
    
    ErrorCode CreateDefault(string filePath);

    ref PlaylistRecord GetPlaylist();
}