using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Music;

namespace SmartVoiceAgent.Infrastructure.Factories
{
    public static class MusicServiceFactory
    {
        public static IMusicService Create()
        {
            if (OperatingSystem.IsWindows())
                return new WindowsMusicService();
            if (OperatingSystem.IsLinux())
                return new LinuxMusicService();
            if (OperatingSystem.IsMacOS())
                return new MacOSMusicService();

            throw new PlatformNotSupportedException("Unsupported OS platform.");
        }
    }
}
