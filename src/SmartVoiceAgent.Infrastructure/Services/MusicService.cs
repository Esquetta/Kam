using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Dummy implementation for music service.
/// </summary>
public class MusicService : IMusicService
{
    public Task PlayMusicAsync(string filePath, bool loop = false, CancellationToken cancellationToken = default)
    {
        // TODO: Müzik çalma entegrasyonu yapılacak.
        return Task.CompletedTask;
    }

    public Task PauseMusicAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ResumeMusicAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopMusicAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Müzik çalma entegrasyonu yapılacak.
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
