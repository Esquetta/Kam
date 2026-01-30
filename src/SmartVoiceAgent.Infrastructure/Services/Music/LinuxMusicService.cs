using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    /// <summary>
    /// Linux implementation of music service with file search capability
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
        private readonly string[] _musicExtensions = { ".mp3", ".wav", ".flac", ".ogg", ".aac", ".m4a", ".wma" };

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

        public async Task PlayMusicAsync(string fileNameOrPath, bool loop = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Resolve file path (search if only name provided)
            var resolvedPath = ResolveMusicFilePath(fileNameOrPath);
            if (resolvedPath == null)
            {
                throw new FileNotFoundException($"Music file not found: {fileNameOrPath}");
            }

            await StopMusicAsync(cancellationToken).ConfigureAwait(false);

            _currentFilePath = resolvedPath;
            _isLooping = loop;
            _isPaused = false;

            await StartPlaybackAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a music file path. If a full path is provided, it checks if it exists.
        /// If just a filename is provided, searches common music directories recursively.
        /// </summary>
        private string? ResolveMusicFilePath(string fileNameOrPath)
        {
            // If it's a full path, check if it exists directly
            if (Path.IsPathRooted(fileNameOrPath) || fileNameOrPath.Contains('/'))
            {
                if (File.Exists(fileNameOrPath))
                {
                    _logger?.LogDebug("Using provided full path: {Path}", fileNameOrPath);
                    return fileNameOrPath;
                }
                return null;
            }

            // It's just a filename, search in common music directories
            var searchName = fileNameOrPath;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(searchName);
            var providedExt = Path.GetExtension(searchName).ToLowerInvariant();
            
            _logger?.LogInformation("🔍 Searching for music file: {Name}", searchName);

            // Build list of root directories to search
            var rootDirectories = new List<string>();
            
            // User's home directory Music folder
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homeDir))
            {
                var userMusic = Path.Combine(homeDir, "Music");
                if (Directory.Exists(userMusic))
                {
                    rootDirectories.Add(userMusic);
                    _logger?.LogDebug("Added search directory: {Dir}", userMusic);
                }
                
                // Also check Downloads
                var downloads = Path.Combine(homeDir, "Downloads");
                if (Directory.Exists(downloads))
                {
                    rootDirectories.Add(downloads);
                    _logger?.LogDebug("Added search directory: {Dir}", downloads);
                }
            }

            // Common Linux music locations
            var commonPaths = new[]
            {
                "/home/*/Music",
                "/home/*/Downloads",
                "/usr/share/music",
                "/var/lib/music",
                "/media/*/Music"
            };

            foreach (var pathPattern in commonPaths)
            {
                try
                {
                    if (pathPattern.Contains('*'))
                    {
                        // Handle wildcard patterns
                        var basePath = pathPattern.Substring(0, pathPattern.IndexOf('*'));
                        if (Directory.Exists(basePath))
                        {
                            var dirs = Directory.GetDirectories(basePath);
                            foreach (var dir in dirs)
                            {
                                var fullPath = dir + pathPattern.Substring(pathPattern.IndexOf('*') + 1);
                                if (Directory.Exists(fullPath) && !rootDirectories.Contains(fullPath))
                                {
                                    rootDirectories.Add(fullPath);
                                    _logger?.LogDebug("Added search directory: {Dir}", fullPath);
                                }
                            }
                        }
                    }
                    else if (Directory.Exists(pathPattern) && !rootDirectories.Contains(pathPattern))
                    {
                        rootDirectories.Add(pathPattern);
                        _logger?.LogDebug("Added search directory: {Dir}", pathPattern);
                    }
                }
                catch { /* Ignore errors */ }
            }

            // Search each root directory recursively (up to 3 levels deep for performance)
            foreach (var rootDir in rootDirectories)
            {
                try
                {
                    var result = SearchDirectoryRecursive(rootDir, searchName, nameWithoutExt, providedExt, 0, 3);
                    if (result != null)
                    {
                        _logger?.LogInformation("✅ Found music file: {Path}", result);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Error searching directory: {Directory}", rootDir);
                }
            }

            _logger?.LogWarning("❌ Music file not found: {Name}", searchName);
            _logger?.LogInformation("💡 Searched in: {Dirs}", string.Join(", ", rootDirectories));
            return null;
        }

        /// <summary>
        /// Recursively searches a directory for a music file
        /// </summary>
        private string? SearchDirectoryRecursive(string directory, string searchName, string nameWithoutExt, string providedExt, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth)
                return null;

            try
            {
                // Check files in current directory
                // If extension was provided, search for exact match first (case-insensitive)
                if (!string.IsNullOrEmpty(providedExt))
                {
                    var exactPath = Path.Combine(directory, searchName);
                    if (File.Exists(exactPath))
                    {
                        return exactPath;
                    }

                    // Case-insensitive search
                    var files = Directory.GetFiles(directory);
                    foreach (var file in files)
                    {
                        if (string.Equals(Path.GetFileName(file), searchName, StringComparison.OrdinalIgnoreCase))
                        {
                            return file;
                        }
                    }
                }

                // Search with any supported extension (case-insensitive)
                foreach (var ext in _musicExtensions)
                {
                    var filePath = Path.Combine(directory, nameWithoutExt + ext);
                    if (File.Exists(filePath))
                    {
                        return filePath;
                    }

                    // Case-insensitive filename comparison
                    var files = Directory.GetFiles(directory, "*" + ext, SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        if (string.Equals(Path.GetFileNameWithoutExtension(file), nameWithoutExt, StringComparison.OrdinalIgnoreCase))
                        {
                            return file;
                        }
                    }
                }

                // Search subdirectories
                var subDirs = Directory.GetDirectories(directory);
                foreach (var subDir in subDirs)
                {
                    var result = SearchDirectoryRecursive(subDir, searchName, nameWithoutExt, providedExt, currentDepth + 1, maxDepth);
                    if (result != null)
                        return result;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error searching directory: {Directory}", directory);
            }

            return null;
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
