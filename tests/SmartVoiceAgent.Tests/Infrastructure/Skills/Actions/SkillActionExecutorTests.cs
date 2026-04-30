using FluentAssertions;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Actions;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Actions;

public sealed class SkillActionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RunsOpenAppAndDesktopActionsInOrder()
    {
        var appService = new RecordingApplicationService();
        var desktop = new RecordingDesktopAutomationAdapter();
        var executor = new SkillActionExecutor(
            new RecordingApplicationServiceFactory(appService),
            desktop);
        var plan = new SkillActionPlan
        {
            Message = "Done",
            Actions =
            [
                new SkillActionStep { Type = SkillActionTypes.OpenApp, ApplicationName = "notepad" },
                new SkillActionStep { Type = SkillActionTypes.Hotkey, Keys = ["ctrl", "l"] },
                new SkillActionStep { Type = SkillActionTypes.TypeText, Text = "hello" },
                new SkillActionStep { Type = SkillActionTypes.Click, X = 100, Y = 200 }
            ]
        };

        var result = await executor.ExecuteAsync(plan);

        result.Success.Should().BeTrue();
        appService.OpenedApplications.Should().Equal("notepad");
        desktop.Calls.Should().Equal(
            "hotkey:ctrl+l",
            "type:hello",
            "click:100,200");
        result.Message.Should().Contain("Done");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownActionFailsWithoutRunningRemainingSteps()
    {
        var appService = new RecordingApplicationService();
        var desktop = new RecordingDesktopAutomationAdapter();
        var executor = new SkillActionExecutor(
            new RecordingApplicationServiceFactory(appService),
            desktop);
        var plan = new SkillActionPlan
        {
            Actions =
            [
                new SkillActionStep { Type = "unknown" },
                new SkillActionStep { Type = SkillActionTypes.OpenApp, ApplicationName = "notepad" }
            ]
        };

        var result = await executor.ExecuteAsync(plan);

        result.Success.Should().BeFalse();
        appService.OpenedApplications.Should().BeEmpty();
        result.Message.Should().Contain("Unsupported action");
    }

    private sealed class RecordingDesktopAutomationAdapter : IDesktopAutomationAdapter
    {
        public List<string> Calls { get; } = [];

        public Task<SkillActionStepResult> ClickAsync(int x, int y, CancellationToken cancellationToken = default)
        {
            Calls.Add($"click:{x},{y}");
            return Task.FromResult(SkillActionStepResult.Succeeded(SkillActionTypes.Click, "clicked"));
        }

        public Task<SkillActionStepResult> TypeTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls.Add($"type:{text}");
            return Task.FromResult(SkillActionStepResult.Succeeded(SkillActionTypes.TypeText, "typed"));
        }

        public Task<SkillActionStepResult> HotkeyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            Calls.Add($"hotkey:{string.Join("+", keys)}");
            return Task.FromResult(SkillActionStepResult.Succeeded(SkillActionTypes.Hotkey, "hotkey"));
        }

        public Task<SkillActionStepResult> FocusWindowAsync(string target, CancellationToken cancellationToken = default)
        {
            Calls.Add($"focus:{target}");
            return Task.FromResult(SkillActionStepResult.Succeeded(SkillActionTypes.FocusWindow, "focused"));
        }
    }

    private sealed class RecordingApplicationServiceFactory : IApplicationServiceFactory
    {
        private readonly IApplicationService _service;

        public RecordingApplicationServiceFactory(IApplicationService service)
        {
            _service = service;
        }

        public IApplicationService Create() => _service;
    }

    private sealed class RecordingApplicationService : IApplicationService
    {
        public List<string> OpenedApplications { get; } = [];

        public Task OpenApplicationAsync(string appName)
        {
            OpenedApplications.Add(appName);
            return Task.CompletedTask;
        }

        public Task<SmartVoiceAgent.Core.Enums.AppStatus> GetApplicationStatusAsync(string appName) =>
            Task.FromResult(SmartVoiceAgent.Core.Enums.AppStatus.Running);

        public Task CloseApplicationAsync(string appName) => Task.CompletedTask;

        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync() =>
            Task.FromResult<IEnumerable<AppInfoDTO>>([]);

        public Task<ApplicationInstallInfo> CheckApplicationInstallationAsync(string appName) =>
            Task.FromResult(new ApplicationInstallInfo(true, appName, appName));

        public Task<string?> GetApplicationExecutablePathAsync(string appName) =>
            Task.FromResult<string?>(appName);
    }
}
