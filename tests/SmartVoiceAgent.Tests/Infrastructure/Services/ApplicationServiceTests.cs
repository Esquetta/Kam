using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public class ApplicationServiceTests
{
    [Fact]
    public async Task InterfaceStatusCheck_DelegatesToInnerService()
    {
        var innerService = new RecordingApplicationService(AppStatus.Suspended);
        IApplicationService service = new ApplicationService(innerService);

        var status = await service.GetApplicationStatusAsync("spotify");

        Assert.Equal(AppStatus.Suspended, status);
        Assert.Equal("spotify", innerService.StatusCheckAppName);
    }

    private sealed class RecordingApplicationService(AppStatus status) : IApplicationService
    {
        public string? StatusCheckAppName { get; private set; }

        public Task OpenApplicationAsync(string appName) => Task.CompletedTask;

        public Task<AppStatus> GetApplicationStatusAsync(string appName)
        {
            StatusCheckAppName = appName;
            return Task.FromResult(status);
        }

        public Task CloseApplicationAsync(string appName) => Task.CompletedTask;

        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync() =>
            Task.FromResult<IEnumerable<AppInfoDTO>>(Array.Empty<AppInfoDTO>());

        public Task<ApplicationInstallInfo> CheckApplicationInstallationAsync(string appName) =>
            Task.FromResult(new ApplicationInstallInfo(false, string.Empty, appName));

        public Task<string> GetApplicationExecutablePathAsync(string appName) =>
            Task.FromResult(string.Empty);
    }
}
