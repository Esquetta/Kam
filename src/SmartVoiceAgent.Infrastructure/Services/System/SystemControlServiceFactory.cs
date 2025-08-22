using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.System
{
    public class SystemControlServiceFactory : ISystemControlServiceFactory
    {
        public ISystemControlService CreateSystemService()
        {
            if (OperatingSystem.IsWindows())
                return new WindowsSystemControlService();
            if (OperatingSystem.IsLinux())
                return new LinuxSystemControlService();
            if (OperatingSystem.IsMacOS())
                return new MacOSSystemControlService();

            throw new PlatformNotSupportedException("Unsupported OS platform.");
        }
    }
}
