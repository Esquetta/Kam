using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    /// <summary>
    /// Linux implementation of music service with proper process management
    /// </summary>
    public class LinuxMusicService : IMusicService, IDisposable
    {
        private Process? _currentProcess;
        private bool _isLooping;
        private float _volume = 1.0f;
        private string? _currentFilePath;
        private bool _isPaused;
        private Timer? _loopTimer;
        private readonly string _preferredPlayer;
        private bool _disposed;
        private readonly ILogger<LinuxMusicService>? _logger;

        public LinuxMusicService(ILogger<LinuxMusicService>? logger = null)
        {
            _logger = logger;
            _preferredPlayer = DetectAvailablePlayer();
        }

        private string DetectAvailablePlayer()
        {
            var players = new[] { "ffplay", "mpg123", "mplayer" };

            foreach (var player in players)
            {
                try
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = player,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    });

                    process?.WaitForExit(5000);
                    if (process?.ExitCode == 0)
                    {
                        return player;
                    }
                }
                catch
                {
                    // Continue to next player
                }
            }

            return "ffplay"; // Default fallback
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
                var processStartInfo = CreateProcessStartInfo();

                _currentProcess = Process.Start(processStartInfo);

                if (_currentProcess != null)
                {
                    _currentProcess.EnableRaisingEvents = true;
                    _currentProcess.Exited += OnProcessExited;
                }

                _logger?.LogDebug("Started playback on Linux using {Player}", _preferredPlayer);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start playback");
                await CleanupAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"Music playback failed: {ex.Message}", ex);
            }
        }

        private ProcessStartInfo CreateProcessStartInfo()
        {
            var processStartInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };

            switch (_preferredPlayer)
            {
                case "ffplay":
                    processStartInfo.FileName = "ffplay";
                    processStartInfo.Arguments = $"-nodisp -autoexit -volume {(int)(_volume * 100)} \"{_currentFilePath}\"";
                    break;

                case "mpg123":
                    processStartInfo.FileName = "mpg123";
                    processStartInfo.Arguments = $"-g {(int)(_volume * 100)} \"{_currentFilePath}\"";
                    break;

                case "mplayer":
                    processStartInfo.FileName = "mplayer";
                    processStartInfo.Arguments = $"-volume {(int)(_volume * 100)} -really-quiet \"{_currentFilePath}\"";
                    break;
            }

            return processStartInfo;
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (_disposed) return;

            // Unsubscribe immediately to prevent multiple callbacks
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

                try
                {
                    using var pauseProcess = Process.Start("kill", $"-STOP {_currentProcess.Id}");
                    pauseProcess?.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to pause process, killing instead");
                    _currentProcess.Kill();
                    _currentProcess.Dispose();
                    _currentProcess = null;
                }
            }
            return Task.CompletedTask;
        }

        public Task ResumeMusicAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_isPaused)
            {
                _isPaused = false;

                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    try
                    {
                        using var resumeProcess = Process.Start("kill", $"-CONT {_currentProcess.Id}");
                        resumeProcess?.WaitForExit(1000);
                        return Task.CompletedTask;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to resume process");
                    }
                }

                // Restart if needed
                if (_currentFilePath != null)
                {
                    return StartPlaybackAsync(cancellationToken);
                }
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
                // Unsubscribe first
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
