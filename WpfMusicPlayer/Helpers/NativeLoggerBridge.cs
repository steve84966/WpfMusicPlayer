using Microsoft.Extensions.Logging;

namespace WpfMusicPlayer.Helpers;

public class NativeLoggerBridge
{
    private readonly ILogger _logger;

    public NativeLoggerBridge(ILogger<NativeLoggerBridge> logger)
    {
        _logger = logger;
    }

    // for native method to invoke using reflection
    public void LogTrace(string message) => _logger.LogTrace(message);
    public void LogDebug(string message) => _logger.LogDebug(message);
    public void LogInformation(string message) => _logger.LogInformation(message);
    public void LogWarning(string message) => _logger.LogWarning(message);
    public void LogError(string message) => _logger.LogError(message);
}
