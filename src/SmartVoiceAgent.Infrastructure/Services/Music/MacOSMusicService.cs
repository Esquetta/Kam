using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    /// <summary>
    /// macOS implementation of music service with file search capability
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
        private readonly string[] _musicExtensions = { ".mp3", ".wav", ".flac", ".ogg", ".aac", ".m4a", ".wma" };

        public MacOSMusicService(ILogger<MacOSMusicService>? logger = null)
        {
            _logger = logger;
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
                
                // Also check Music/iTunes/iTunes Media/Music (common iTunes location)
                var itunesMusic = Path.Combine(homeDir, "Music", "iTunes", "iTunes Media", "Music");
                if (Directory.Exists(itunesMusic))
                {
                    rootDirectories.Add(itunesMusic);
                    _logger?.LogDebug("Added search directory: {Dir}", itunesMusic);
                }
                
                // Downloads folder
                var downloads = Path.Combine(homeDir, "Downloads");
                if (Directory.Exists(downloads))
                {
                    rootDirectories.Add(downloads);
                    _logger?.LogDebug("Added search directory: {Dir}", downloads);
                }
            }

            // Common macOS music locations
            var commonPaths = new[]
            {
                "/Users/Shared/Music",
                "/Library/Music"
            };

            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path) && !rootDirectories.Contains(path))
                {
                    rootDirectories.Add(path);
                    _logger?.LogDebug("Added search directory: {Dir}", path);
                }
            }

            // Search each root directory recursively (up to 4 levels deep for iTunes structure)
            foreach (var rootDir in rootDirectories)
            {
                try
                {
                    var result = SearchDirectoryRecursive(rootDir, searchName, nameWithoutExt, providedExt, 0, 4);
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
