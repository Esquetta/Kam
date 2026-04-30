using System.Drawing;
using FluentAssertions;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Dtos.Screen;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Context;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Context;

public sealed class SkillRuntimeContextProviderTests
{
    [Fact]
    public async Task CreateAsync_BuildsBoundedWindowScreenAndApplicationContext()
    {
        var activeWindow = new ActiveWindowInfo
        {
            Title = "Settings",
            ProcessName = "SystemSettings",
            ProcessId = 42,
            WindowBounds = new Rectangle(10, 20, 800, 600),
            HasFocus = true
        };
        var provider = new SkillRuntimeContextProvider(
            new FakeActiveWindowService(activeWindow),
            new FakeScreenContextService(),
            new FakeApplicationServiceFactory());

        var context = await provider.CreateAsync(SkillPlan.FromObject(
            "local.desktop-navigation",
            new { input = "Open display settings" }));

        context.UserInput.Should().Be("Open display settings");
        context.OperatingSystem.Should().NotBeNullOrWhiteSpace();
        context.ActiveWindow.Should().NotBeNull();
        context.ActiveWindow!.Title.Should().Be("Settings");
        context.VisibleWindows.Should().ContainSingle(window => window.Title == "Settings");
        context.Screens.Should().ContainSingle();
        context.Screens[0].OcrLines.Should().Contain("Display");
        context.InstalledApplications.Should().Contain("Notepad");
    }

    private sealed class FakeActiveWindowService : IActiveWindowService
    {
        private readonly ActiveWindowInfo _activeWindow;

        public FakeActiveWindowService(ActiveWindowInfo activeWindow)
        {
            _activeWindow = activeWindow;
        }

        public Task<ActiveWindowInfo?> GetActiveWindowInfoAsync()
        {
            return Task.FromResult<ActiveWindowInfo?>(_activeWindow);
        }

        public Task<IEnumerable<ActiveWindowInfo>> GetAllWindowsAsync()
        {
            return Task.FromResult<IEnumerable<ActiveWindowInfo>>([_activeWindow]);
        }
    }

    private sealed class FakeScreenContextService : IScreenContextService
    {
        public Task<List<ScreenContext>> CaptureAndAnalyzeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<List<ScreenContext>>(
            [
                new ScreenContext
                {
                    ScreenIndex = 0,
                    DeviceName = "Primary",
                    Width = 1920,
                    Height = 1080,
                    IsPrimary = true,
                    ScreenshotHash = "hash",
                    Timestamp = DateTimeOffset.UtcNow,
                    OcrLines =
                    [
                        new OcrLine(1, "Display", 0.95, new Rectangle(10, 10, 100, 20))
                    ]
                }
            ]);
        }
    }

    private sealed class FakeApplicationServiceFactory : IApplicationServiceFactory
    {
        public IApplicationService Create()
        {
            return new FakeApplicationService();
        }
    }

    private sealed class FakeApplicationService : IApplicationService
    {
        public Task OpenApplicationAsync(string appName) => Task.CompletedTask;

        public Task<SmartVoiceAgent.Core.Enums.AppStatus> GetApplicationStatusAsync(string appName) =>
            Task.FromResult(SmartVoiceAgent.Core.Enums.AppStatus.Running);

        public Task CloseApplicationAsync(string appName) => Task.CompletedTask;

        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync()
        {
            return Task.FromResult<IEnumerable<AppInfoDTO>>(
            [
                new AppInfoDTO("Notepad", "", false),
                new AppInfoDTO("Calculator", "", false)
            ]);
        }

        public Task<ApplicationInstallInfo> CheckApplicationInstallationAsync(string appName) =>
            Task.FromResult(new ApplicationInstallInfo(true, "notepad.exe", appName));

        public Task<string?> GetApplicationExecutablePathAsync(string appName) =>
            Task.FromResult<string?>("notepad.exe");
    }
}
