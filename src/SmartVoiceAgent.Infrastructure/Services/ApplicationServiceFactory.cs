using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Application;

public class ApplicationServiceFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ApplicationServiceFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IApplicationService Create()
    {
        if (OperatingSystem.IsWindows())
            return _serviceProvider.GetRequiredService<WindowsApplicationService>();
        if (OperatingSystem.IsLinux())
            return _serviceProvider.GetRequiredService<LinuxApplicationService>();

        throw new PlatformNotSupportedException("Unsupported OS platform.");
    }
}
