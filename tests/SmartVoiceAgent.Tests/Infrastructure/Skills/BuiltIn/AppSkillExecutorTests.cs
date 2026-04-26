using FluentAssertions;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AppSkills;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class AppSkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AppsStatus_ReturnsRunningStatus()
    {
        var executor = new AppSkillExecutor(new FakeApplicationService());
        var plan = SkillPlan.FromObject("apps.status", new { applicationName = "Spotify" });

        var result = await executor.ExecuteAsync(plan);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Running");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedSkill_ReturnsFailure()
    {
        var executor = new AppSkillExecutor(new FakeApplicationService());
        var result = await executor.ExecuteAsync(
            SkillPlan.FromObject("apps.delete", new { applicationName = "Spotify" }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported app skill");
    }

    private sealed class FakeApplicationService : IApplicationService
    {
        public Task OpenApplicationAsync(string appName) => Task.CompletedTask;

        public Task<AppStatus> GetApplicationStatusAsync(string appName) => Task.FromResult(AppStatus.Running);

        public Task CloseApplicationAsync(string appName) => Task.CompletedTask;

        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync() =>
            Task.FromResult<IEnumerable<AppInfoDTO>>([new("Spotify", "spotify.exe", true)]);

        public Task<ApplicationInstallInfo> CheckApplicationInstallationAsync(string appName) =>
            Task.FromResult(new ApplicationInstallInfo(true, "spotify.exe", appName));

        public Task<string> GetApplicationExecutablePathAsync(string appName) => Task.FromResult("spotify.exe");
    }
}
