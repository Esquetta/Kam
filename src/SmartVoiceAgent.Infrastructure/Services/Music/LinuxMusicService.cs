using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    public class LinuxMusicService : IMusicService, IDisposable
    {
        private Process? _currentProcess;
        private bool _isLooping;
        private float _volume = 1.0f;
        private string? _currentFilePath;
        private bool _isPaused;
        private Timer? _loopTimer;
        private string _preferredPlayer;

        public LinuxMusicService()
        {
            _preferredPlayer = DetectAvailablePlayer();
        }

        private string DetectAvailablePlayer()
        {
            var players = new[] { "ffplay", "mpg123", "mplayer" };

            foreach (var player in players)
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = player,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    });

                    process?.WaitForExit();
                    if (process?.ExitCode == 0)
                    {
                        return player;
                    }
                }
                catch
                {
                    // Devam et
                }
            }

            return "ffplay"; // Varsayılan
        }

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
                var processStartInfo = CreateProcessStartInfo();

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
            if (_isLooping && !_isPaused && _currentFilePath != null)
            {
                _loopTimer = new Timer(_ => StartPlayback(), null, 100, Timeout.Infinite);
            }
        }

        public Task PauseMusicAsync()
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _isPaused = true;

                try
                {
                    // Çoğu player SIGSTOP ile pause edilebilir
                    Process.Start("kill", $"-STOP {_currentProcess.Id}");
                }
                catch
                {
                    // Eğer kill komutu çalışmazsa process'i durdur
                    _currentProcess.Kill();
                    _currentProcess = null;
                }
            }
            return Task.CompletedTask;
        }

        public Task ResumeMusicAsync()
        {
            if (_isPaused)
            {
                _isPaused = false;

                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    try
                    {
                        // SIGCONT ile devam ettir
                        Process.Start("kill", $"-CONT {_currentProcess.Id}");
                        return Task.CompletedTask;
                    }
                    catch
                    {
                        // Kill komutu çalışmazsa yeniden başlat
                    }
                }

                // Process yoksa veya kill komutu çalışmazsa yeniden başlat
                if (_currentFilePath != null)
                {
                    return StartPlayback();
                }
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