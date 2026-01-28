using BenchmarkDotNet.Attributes;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Benchmarks;

/// <summary>
/// Benchmarks for MusicService operations
/// </summary>
[MemoryDiagnoser]
public class MusicServiceBenchmark
{
    private IMusicService _musicService = null!;
    private const string TestFilePath = "test.mp3";

    [GlobalSetup]
    public void Setup()
    {
        _musicService = new FakeMusicService();
    }

    [Benchmark]
    public async Task PlayMusicAsync()
    {
        await _musicService.PlayMusicAsync(TestFilePath, loop: false);
    }

    [Benchmark]
    public async Task PlayMusicWithLoopAsync()
    {
        await _musicService.PlayMusicAsync(TestFilePath, loop: true);
    }

    [Benchmark]
    public async Task StopMusicAsync()
    {
        await _musicService.StopMusicAsync();
    }

    [Benchmark]
    public async Task PauseMusicAsync()
    {
        await _musicService.PauseMusicAsync();
    }

    [Benchmark]
    public async Task ResumeMusicAsync()
    {
        await _musicService.ResumeMusicAsync();
    }

    [Benchmark]
    [Arguments(0.0f)]
    [Arguments(0.5f)]
    [Arguments(1.0f)]
    public async Task SetVolumeAsync(float volume)
    {
        await _musicService.SetVolumeAsync(volume);
    }

    [Benchmark]
    public async Task FullPlaybackCycle()
    {
        await _musicService.PlayMusicAsync(TestFilePath);
        await _musicService.SetVolumeAsync(0.5f);
        await _musicService.PauseMusicAsync();
        await _musicService.ResumeMusicAsync();
        await _musicService.StopMusicAsync();
    }

    [Benchmark]
    public async Task ConcurrentOperations()
    {
        var tasks = new List<Task>
        {
            _musicService.PlayMusicAsync(TestFilePath),
            _musicService.SetVolumeAsync(0.7f),
            _musicService.PauseMusicAsync()
        };

        await Task.WhenAll(tasks);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_musicService as IDisposable)?.Dispose();
    }
}

/// <summary>
/// Benchmark for comparing different music service implementations
/// </summary>
[MemoryDiagnoser]
public class MusicServiceComparisonBenchmark
{
    [ParamsSource(nameof(MusicServiceImplementations))]
    public IMusicService MusicService { get; set; } = null!;

    public IEnumerable<IMusicService> MusicServiceImplementations => new[]
    {
        new FakeMusicService()
    };

    [Benchmark]
    public async Task PlayAndStop()
    {
        await MusicService.PlayMusicAsync("test.mp3");
        await MusicService.StopMusicAsync();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (MusicService as IDisposable)?.Dispose();
    }
}
