namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for music operations.
/// </summary>
public interface IMusicService
{
    /// <summary>
    /// Plays a music track asynchronously.
    /// </summary>
    /// <param name="trackName">The name of the track to play.</param>
    Task PlayMusicAsync(string trackName);
}
