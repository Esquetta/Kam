using NAudio.Wave;
using Microsoft.Extensions.Logging;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    /// <summary>
    /// Windows implementation of music service with file search capability
    /// </summary>
    public class WindowsMusicService : IMusicService, IDisposable
    {
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioFileReader;
        private bool _isLooping;
        private float _volume = 1.0f;
        private bool _disposed;
        private readonly ILogger<WindowsMusicService>? _logger;
        private readonly string[] _musicExtensions = { ".mp3", ".wav", ".flac", ".ogg", ".aac", ".m4a", ".wma" };

        public WindowsMusicService(ILogger<WindowsMusicService>? logger = null)
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

            // Stop any existing playback properly
            await StopMusicAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                _audioFileReader = new AudioFileReader(resolvedPath)
                {
                    Volume = _volume
                };

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioFileReader);
                _isLooping = loop;
                
                // Subscribe to event
                _waveOut.PlaybackStopped += OnPlaybackStopped;

                _waveOut.Play();
                _logger?.LogDebug("Started playing: {FilePath}, Loop: {Loop}", resolvedPath, loop);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to play music: {FilePath}", resolvedPath);
                await CleanupAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Resolves a music file path. If a full path is provided, it checks if it exists.
        /// If just a filename is provided, searches common music directories recursively.
        /// </summary>
        private string? ResolveMusicFilePath(string fileNameOrPath)
        {
            // If it's a full path, check if it exists directly
            if (Path.IsPathRooted(fileNameOrPath) || fileNameOrPath.Contains(Path.DirectorySeparatorChar))
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
            
            // Remove extension if provided to allow searching with different extensions
            var nameWithoutExt = Path.GetFileNameWithoutExtension(searchName);
            var providedExt = Path.GetExtension(searchName).ToLowerInvariant();
            
            _logger?.LogInformation("🔍 Searching for music file: {Name}", searchName);

            // Build list of root directories to search
            var rootDirectories = new List<string>();
            
            // User's Music folder
            var userMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrEmpty(userMusic) && Directory.Exists(userMusic))
            {
                rootDirectories.Add(userMusic);
                _logger?.LogDebug("Added search directory: {Dir}", userMusic);
            }

            // Common Windows music locations
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonMusic)),
                @"C:\Users\Public\Music",
                @"C:\Music"
            };

            foreach (var path in commonPaths)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !rootDirectories.Contains(path))
                {
                    rootDirectories.Add(path);
                    _logger?.LogDebug("Added search directory: {Dir}", path);
                }
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
                // If extension was provided, search for exact match first
                if (!string.IsNullOrEmpty(providedExt))
                {
                    var exactPath = Path.Combine(directory, searchName);
                    if (File.Exists(exactPath))
                    {
                        return exactPath;
                    }

                    // Also try case-insensitive comparison
                    var files = Directory.GetFiles(directory);
                    foreach (var file in files)
                    {
                        if (string.Equals(Path.GetFileName(file), searchName, StringComparison.OrdinalIgnoreCase))
                        {
                            return file;
                        }
                    }
                }

                // Search with any supported extension
                foreach (var ext in _musicExtensions)
                {
                    var filePath = Path.Combine(directory, nameWithoutExt + ext);
                    if (File.Exists(filePath))
                    {
                        return filePath;
                    }

                    // Also try case-insensitive
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
