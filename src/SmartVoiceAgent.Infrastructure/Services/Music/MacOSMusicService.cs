using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    public class MacOSMusicService : IMusicService
    {
        private Process? _musicProcess;

        public Task PlayMusicAsync(string filePath)
        {
            _musicProcess = Process.Start("open", filePath);
            return Task.CompletedTask;
        }

        public Task StopMusicAsync()
        {
            _musicProcess?.Kill();
            return Task.CompletedTask;
        }
    }
}
