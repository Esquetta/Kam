using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Backward-compatible application service wrapper that delegates to the current platform implementation.
/// </summary>
public sealed class ApplicationService : IApplicationService
{
    private readonly IApplicationService _innerService;

    public ApplicationService()
        : this(new ApplicationServiceFactory().Create())
    {
    }

    public ApplicationService(IApplicationService innerService)
    {
        _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
    }

    public Task OpenApplicationAsync(string appName) =>
        _innerService.OpenApplicationAsync(appName);

    public Task<AppStatus> GetApplicationStatusAsync(string appName) =>
        _innerService.GetApplicationStatusAsync(appName);

    public Task CloseApplicationAsync(string appName) =>
        _innerService.CloseApplicationAsync(appName);

    public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync() =>
        _innerService.ListApplicationsAsync();

    public Task<ApplicationInstallInfo> CheckApplicationInstallationAsync(string appName) =>
        _innerService.CheckApplicationInstallationAsync(appName);

    public Task<string?> GetApplicationExecutablePathAsync(string appName) =>
        _innerService.GetApplicationExecutablePathAsync(appName);
}
