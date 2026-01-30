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
        /// If just a filename is provided, searches common music directories.
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
            
            _logger?.LogDebug("Searching for music file: {Name}", searchName);

            // Build list of directories to search
            var searchDirectories = new List<string>();
            
            // User's Music folder
            var userMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrEmpty(userMusic) && Directory.Exists(userMusic))
            {
                searchDirectories.Add(userMusic);
                // Also add subdirectories (one level deep)
                try
                {
                    searchDirectories.AddRange(Directory.GetDirectories(userMusic));
                }
                catch { /* Ignore access errors */ }
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
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !searchDirectories.Contains(path))
                {
                    searchDirectories.Add(path);
                }
            }

            // Search for the file
            foreach (var directory in searchDirectories)
            {
                try
                {
                    // If extension was provided, search for exact match first
                    if (!string.IsNullOrEmpty(providedExt))
                    {
                        var exactPath = Path.Combine(directory, searchName);
                        if (File.Exists(exactPath))
                        {
                            _logger?.LogInformation("Found music file: {Path}", exactPath);
                            return exactPath;
                        }
                    }

                    // Search with any supported extension
                    foreach (var ext in _musicExtensions)
                    {
                        var filePath = Path.Combine(directory, nameWithoutExt + ext);
                        if (File.Exists(filePath))
                        {
                            _logger?.LogInformation("Found music file: {Path}", filePath);
                            return filePath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Error searching directory: {Directory}", directory);
                }
            }

            _logger?.LogWarning("Music file not found: {Name}", searchName);
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
