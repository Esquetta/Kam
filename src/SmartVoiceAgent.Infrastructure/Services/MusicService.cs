using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Dummy implementation for music service.
/// </summary>
public class MusicService : IMusicService
{
    public Task PlayMusicAsync(string trackName)
    {
        // TODO: Müzik çalma entegrasyonu yapılacak.
        return Task.CompletedTask;
    }
}
