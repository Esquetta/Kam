using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Dummy implementation for music service.
/// </summary>
public class MusicService : IMusicService
{
    public Task PauseMusicAsync()
    {
        throw new NotImplementedException();
    }

    public Task PlayMusicAsync(string trackName)
    {
        // TODO: Müzik çalma entegrasyonu yapılacak.
        return Task.CompletedTask;
    }

    public Task PlayMusicAsync(string filePath, bool loop = false)
    {
        throw new NotImplementedException();
    }

    public Task ResumeMusicAsync()
    {
        throw new NotImplementedException();
    }

    public Task SetVolumeAsync(float volume)
    {
        throw new NotImplementedException();
    }

    public Task StopMusicAsync()
    {
        // TODO: Müzik çalma entegrasyonu yapılacak.
        return Task.CompletedTask;
    }
}
