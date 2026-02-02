using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Benchmarks.Fakes;

/// <summary>
/// Fake music service for benchmarking - simulates music operations without actual audio playback.
/// Used by BenchmarkDotNet to measure performance of music service operations.
/// </summary>
public class FakeMusicService : IMusicService, IDisposable
{
    private bool _isPlaying;
    private bool _isPaused;
    private float _volume = 1.0f;
    private string? _currentFilePath;

    public Task PlayMusicAsync(string filePath, bool loop = false, CancellationToken cancellationToken = default)
    {
        _currentFilePath = filePath;
        _isPlaying = true;
        _isPaused = false;
        return Task.CompletedTask;
    }

    public Task PauseMusicAsync(CancellationToken cancellationToken = default)
    {
        if (_isPlaying)
        {
            _isPaused = true;
        }
        return Task.CompletedTask;
    }

    public Task ResumeMusicAsync(CancellationToken cancellationToken = default)
    {
        if (_isPaused)
        {
            _isPaused = false;
            _isPlaying = true;
        }
        return Task.CompletedTask;
    }

    public Task StopMusicAsync(CancellationToken cancellationToken = default)
    {
        _isPlaying = false;
        _isPaused = false;
        _currentFilePath = null;
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
    {
        _volume = Math.Clamp(volume, 0.0f, 1.0f);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _isPlaying = false;
        _isPaused = false;
        _currentFilePath = null;
    }
}
