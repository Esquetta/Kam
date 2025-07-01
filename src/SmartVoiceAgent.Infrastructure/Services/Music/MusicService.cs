using NAudio.Wave;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    public class MusicService : IMusicService, IDisposable
    {
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioFileReader;
        private bool _isLooping;
        private float _volume = 1.0f;

        public Task PlayMusicAsync(string filePath, bool loop = false)
        {
            StopMusicAsync().Wait(); // Mevcut çalan varsa durdur

            _audioFileReader = new AudioFileReader(filePath)
            {
                Volume = _volume
            };

            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioFileReader);
            _isLooping = loop;
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            _waveOut.Play();
            return Task.CompletedTask;
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (_isLooping)
            {
                _audioFileReader!.Position = 0;
                _waveOut!.Play();
            }
        }

        public Task PauseMusicAsync()
        {
            _waveOut?.Pause();
            return Task.CompletedTask;
        }

        public Task ResumeMusicAsync()
        {
            _waveOut?.Play();
            return Task.CompletedTask;
        }

        public Task StopMusicAsync()
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _audioFileReader?.Dispose();
            _waveOut = null;
            _audioFileReader = null;
            return Task.CompletedTask;
        }

        public Task SetVolumeAsync(float volume)
        {
            _volume = Math.Clamp(volume, 0.0f, 1.0f);
            if (_audioFileReader != null)
                _audioFileReader.Volume = _volume;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            StopMusicAsync().Wait();
        }
    }
}
