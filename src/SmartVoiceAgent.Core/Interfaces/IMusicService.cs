namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for music operations.
/// </summary>
/// <summary>
/// Service for controlling music playback.
/// </summary>
public interface IMusicService
{
    /// <summary>
    /// Plays a specific music track.
    /// </summary>
    Task PlayMusicAsync(string trackName);

    /// <summary>
    /// Stops the current music.
    /// </summary>
    Task StopMusicAsync();

    /// <summary>
    /// Pauses the current music.
    /// </summary>
    Task PauseMusicAsync();

    /// <summary>
    /// Resumes paused music.
    /// </summary>
    Task ResumeMusicAsync();
}
