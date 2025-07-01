using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    public class MacOSMusicService : IMusicService, IDisposable
    {
        private Process? _currentProcess;
        private bool _isLooping;
        private float _volume = 1.0f;
        private string? _currentFilePath;
        private bool _isPaused;
        private Timer? _loopTimer;

        public Task PlayMusicAsync(string filePath, bool loop = false)
        {
            StopMusicAsync().Wait();

            _currentFilePath = filePath;
            _isLooping = loop;
            _isPaused = false;

            return StartPlayback();
        }

        private Task StartPlayback()
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

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Müzik çalınamadı: {ex.Message}", ex);
            }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (_isLooping && !_isPaused && _currentFilePath != null)
            {
                // Kısa bir gecikme ile tekrar başlat
                _loopTimer = new Timer(_ => StartPlayback(), null, 100, Timeout.Infinite);
            }
        }

        public Task PauseMusicAsync()
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _isPaused = true;
                // afplay pause desteklemediği için durdurup pozisyonu saklamamız gerekir
                _currentProcess.Kill();
                _currentProcess = null;
            }
            return Task.CompletedTask;
        }

        public Task ResumeMusicAsync()
        {
            if (_isPaused && _currentFilePath != null)
            {
                _isPaused = false;
                return StartPlayback();
            }
            return Task.CompletedTask;
        }

        public Task StopMusicAsync()
        {
            _isLooping = false;
            _isPaused = false;
            _loopTimer?.Dispose();
            _loopTimer = null;

            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    _currentProcess.Kill();
                    _currentProcess.WaitForExit(1000);
                }
                catch (Exception)
                {
                    // Process zaten öldüyse hata vermesin
                }
                finally
                {
                    _currentProcess?.Dispose();
                    _currentProcess = null;
                }
            }

            return Task.CompletedTask;
        }

        public Task SetVolumeAsync(float volume)
        {
            _volume = Math.Clamp(volume, 0.0f, 1.0f);

            // Eğer şu anda çalıyorsa, yeni volume ile yeniden başlat
            if (_currentProcess != null && !_currentProcess.HasExited && !_isPaused)
            {
                var wasLooping = _isLooping;
                StopMusicAsync().Wait();
                _isLooping = wasLooping;
                return StartPlayback();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            StopMusicAsync().Wait();
            _loopTimer?.Dispose();
        }
    }
}