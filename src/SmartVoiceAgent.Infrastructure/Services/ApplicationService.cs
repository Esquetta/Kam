using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using System.Xml.Linq;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Service for managing application processes.
/// </summary>
public class ApplicationService : IApplicationService
{
    // Buraya platform-specific service bağımlılıkları ekleyebilirsin
    // Örneğin: private readonly IProcessService _processService;

    public Task OpenApplicationAsync(string appName)
    {
        // TODO:
        Console.WriteLine($"{appName} uygulaması açılıyor...");
        return Task.CompletedTask;
    }

    public Task<AppStatus> GetApplicationStatusAsync(string appName)
    {
        // TODO: 
        Console.WriteLine($"{appName} uygulamasının durumu kontrol ediliyor...");
        return Task.FromResult(AppStatus.Running);
    }

    public Task CloseApplicationAsync(string appName)
    {
        // TODO:
        Console.WriteLine($"{appName} uygulaması kapatılıyor...");
        return Task.CompletedTask;
    }

    public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync()
    {
        // TODO:
        var apps = new List<AppInfoDTO>
        {
            new AppInfoDTO("Spotify","../",false),
            new AppInfoDTO("VisualStudio","../",false),
        };

        return Task.FromResult<IEnumerable<AppInfoDTO>>(apps.AsEnumerable());
    }

    Task IApplicationService.OpenApplicationAsync(string appName)
    {
        throw new NotImplementedException();
    }

    Task<AppStatus> IApplicationService.GetApplicationStatusAsync(string appName)
    {
        throw new NotImplementedException();
    }

    Task IApplicationService.CloseApplicationAsync(string appName)
    {
        throw new NotImplementedException();
    }

    Task<IEnumerable<AppInfoDTO>> IApplicationService.ListApplicationsAsync()
    {
        throw new NotImplementedException();
    }

    Task<ApplicationInstallInfo> IApplicationService.CheckApplicationInstallationAsync(string appName)
    {
        throw new NotImplementedException();
    }

    Task<string> IApplicationService.GetApplicationExecutablePathAsync(string appName)
    {
        throw new NotImplementedException();
    }
}
