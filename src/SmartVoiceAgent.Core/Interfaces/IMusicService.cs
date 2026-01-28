/// <summary>
/// Service for playing music/audio files
/// </summary>
public interface IMusicService
{
    /// <summary>
    /// Play music from file
    /// </summary>
    Task PlayMusicAsync(string filePath, bool loop = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Pause current playback
    /// </summary>
    Task PauseMusicAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resume paused playback
    /// </summary>
    Task ResumeMusicAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop playback
    /// </summary>
    Task StopMusicAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Set volume (0.0 - 1.0)
    /// </summary>
    Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default);
}
