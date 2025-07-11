using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Application;

public class ApplicationServiceFactory : IApplicationServiceFactory
{
    public IApplicationService Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsApplicationService();
        if (OperatingSystem.IsLinux())
            return new LinuxApplicationService();

        throw new PlatformNotSupportedException();
    }
}
