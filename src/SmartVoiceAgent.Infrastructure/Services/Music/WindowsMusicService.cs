using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Music
{
    public class WindowsMusicService : IMusicService
    {
        private Process? _musicProcess;

        public Task PlayMusicAsync(string filePath)
        {
            _musicProcess = Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{filePath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            return Task.CompletedTask;
        }

        public Task StopMusicAsync()
        {
            _musicProcess?.Kill();
            return Task.CompletedTask;
        }
    }
}
