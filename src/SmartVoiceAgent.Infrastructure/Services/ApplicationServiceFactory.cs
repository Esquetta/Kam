using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Application;

namespace SmartVoiceAgent.Infrastructure.Services
{
    public static class ApplicationServiceFactory
    {
        public static IApplicationService Create()
        {
            if (OperatingSystem.IsWindows())
                return new WindowsApplicationService();
            if (OperatingSystem.IsLinux())
                return new LinuxApplicationService();
            // if (OperatingSystem.IsMacOS())
            //     return new MacOsApplicationService();

            throw new PlatformNotSupportedException("Unsupported OS platform.");
        }
    }
}
