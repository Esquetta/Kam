using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    /// <summary>
    /// macOS implementation of music service using afplay
    /// </summary>
    public class MacOSMusicService : IMusicService, IDisposable
    {
        private Process? _currentProcess;
        private bool _isLooping;
        private float _volume = 1.0f;
        private string? _currentFilePath;
        private bool _isPaused;
        private Timer? _loopTimer;
        private bool _disposed;
        private readonly ILogger<MacOSMusicService>? _logger;

        public MacOSMusicService(ILogger<MacOSMusicService>? logger = null)
        {
            _logger = logger;
        }

        public async Task PlayMusicAsync(string filePath, bool loop = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await StopMusicAsync(cancellationToken).ConfigureAwait(false);

            _currentFilePath = filePath;
            _isLooping = loop;
            _isPaused = false;

            await StartPlaybackAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task StartPlaybackAsync(CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "afplay",
                    Arguments = $"\"{_currentFilePath}\" -v {_volume}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _currentProcess = Process.Start(processStartInfo);

                if (_currentProcess != null)
                {
                    _currentProcess.EnableRaisingEvents = true;
                    _currentProcess.Exited += OnProcessExited;
                }

                _logger?.LogDebug("Started playback on macOS");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start playback");
                await CleanupAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"Music playback failed: {ex.Message}", ex);
            }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (_disposed) return;

            // Unsubscribe immediately
            if (sender is Process process)
            {
                process.Exited -= OnProcessExited;
            }

            if (_isLooping && !_isPaused && _currentFilePath != null)
            {
                _loopTimer?.Dispose();
                _loopTimer = new Timer(
                    async _ => await StartPlaybackAsync(CancellationToken.None),
                    null,
                    100,
                    Timeout.Infinite);
            }
        }

        public Task PauseMusicAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _isPaused = true;
                // afplay doesn't support pause, so we kill and restart
                try
                {
                    _currentProcess.Exited -= OnProcessExited;
                    _currentProcess.Kill();
                    _currentProcess.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error pausing playback");
                }
                finally
                {
                    _currentProcess = null;
                }
            }
            return Task.CompletedTask;
        }

        public Task ResumeMusicAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_isPaused && _currentFilePath != null)
            {
                _isPaused = false;
                return StartPlaybackAsync(cancellationToken);
            }
            return Task.CompletedTask;
        }

        public async Task StopMusicAsync(CancellationToken cancellationToken = default)
        {
            _isLooping = false;
            _isPaused = false;
            _loopTimer?.Dispose();
            _loopTimer = null;

            await CleanupAsync().ConfigureAwait(false);
        }

        private async Task CleanupAsync()
        {
            if (_currentProcess != null)
            {
                _currentProcess.Exited -= OnProcessExited;

                if (!_currentProcess.HasExited)
                {
                    try
                    {
                        _currentProcess.Kill();
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await _currentProcess.WaitForExitAsync(cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error killing process");
                    }
                }

                _currentProcess.Dispose();
                _currentProcess = null;
            }
        }

        public async Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _volume = Math.Clamp(volume, 0.0f, 1.0f);

            // Restart with new volume if currently playing
            if (_currentProcess != null && !_currentProcess.HasExited && !_isPaused)
            {
                var wasLooping = _isLooping;
                await StopMusicAsync(cancellationToken).ConfigureAwait(false);
                _isLooping = wasLooping;
                await StartPlaybackAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _loopTimer?.Dispose();
            _loopTimer = null;

            try
            {
                if (_currentProcess != null)
                {
                    _currentProcess.Exited -= OnProcessExited;
                    
                    if (!_currentProcess.HasExited)
                    {
                        try { _currentProcess.Kill(); } catch { /* Ignore */ }
                    }
                    
                    _currentProcess.Dispose();
                    _currentProcess = null;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during disposal");
            }
        }
    }
}
