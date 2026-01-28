using NAudio.Wave;
using Microsoft.Extensions.Logging;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    /// <summary>
    /// Windows implementation of music service with proper resource management
    /// </summary>
    public class WindowsMusicService : IMusicService, IDisposable
    {
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioFileReader;
        private bool _isLooping;
        private float _volume = 1.0f;
        private bool _disposed;
        private readonly ILogger<WindowsMusicService>? _logger;

        public WindowsMusicService(ILogger<WindowsMusicService>? logger = null)
        {
            _logger = logger;
        }

        public async Task PlayMusicAsync(string filePath, bool loop = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Stop any existing playback properly
            await StopMusicAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                _audioFileReader = new AudioFileReader(filePath)
                {
                    Volume = _volume
                };

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioFileReader);
                _isLooping = loop;
                
                // Subscribe to event
                _waveOut.PlaybackStopped += OnPlaybackStopped;

                _waveOut.Play();
                _logger?.LogDebug("Started playing: {FilePath}, Loop: {Loop}", filePath, loop);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to play music: {FilePath}", filePath);
                await CleanupAsync().ConfigureAwait(false);
                throw;
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (_disposed) return;

            if (e.Exception != null)
            {
                _logger?.LogError(e.Exception, "Playback stopped with error");
            }

            if (_isLooping && _audioFileReader != null && _waveOut != null)
            {
                try
                {
                    _audioFileReader.Position = 0;
                    _waveOut.Play();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to loop playback");
                }
            }
        }

        public Task PauseMusicAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _waveOut?.Pause();
            return Task.CompletedTask;
        }

        public Task ResumeMusicAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _waveOut?.Play();
            return Task.CompletedTask;
        }

        public Task StopMusicAsync(CancellationToken cancellationToken = default)
        {
            return CleanupAsync();
        }

        private Task CleanupAsync()
        {
            if (_waveOut != null)
            {
                // Unsubscribe from event first to prevent callbacks during cleanup
                _waveOut.PlaybackStopped -= OnPlaybackStopped;
                
                try
                {
                    _waveOut.Stop();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error stopping waveOut");
                }
                
                _waveOut.Dispose();
                _waveOut = null;
            }

            _audioFileReader?.Dispose();
            _audioFileReader = null;

            return Task.CompletedTask;
        }

        public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _volume = Math.Clamp(volume, 0.0f, 1.0f);
            if (_audioFileReader != null)
                _audioFileReader.Volume = _volume;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            try
            {
                // Synchronous cleanup for Dispose
                if (_waveOut != null)
                {
                    _waveOut.PlaybackStopped -= OnPlaybackStopped;
                    try { _waveOut.Stop(); } catch { /* Ignore */ }
                    _waveOut.Dispose();
                    _waveOut = null;
                }
                
                _audioFileReader?.Dispose();
                _audioFileReader = null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during disposal");
            }
        }
    }
}
