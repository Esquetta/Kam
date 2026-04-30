using System.Drawing;
using FluentAssertions;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class WindowContextSkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WindowActive_ReturnsFocusedWindowSummary()
    {
        var executor = new WindowContextSkillExecutor(new FakeActiveWindowService());

        var result = await executor.ExecuteAsync(SkillPlan.FromObject("window.active", new { }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Active Window");
        result.Message.Should().Contain("Kam Editor");
        result.Message.Should().Contain("Code");
    }

    [Fact]
    public async Task ExecuteAsync_WindowList_ReturnsBoundedVisibleWindows()
    {
        var executor = new WindowContextSkillExecutor(new FakeActiveWindowService());

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "window.list",
            new { maxWindows = 1 }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Visible Windows");
        result.Message.Should().Contain("Kam Editor");
        result.Message.Should().NotContain("Browser Docs");
    }

    private sealed class FakeActiveWindowService : IActiveWindowService
    {
        private readonly ActiveWindowInfo _active = new()
        {
            Title = "Kam Editor",
            ProcessName = "Code",
            ProcessId = 42,
            ExecutablePath = "C:\\Apps\\Code.exe",
            WindowBounds = new Rectangle(10, 20, 1200, 800),
            IsVisible = true,
            HasFocus = true
        };

        public Task<ActiveWindowInfo?> GetActiveWindowInfoAsync()
        {
            return Task.FromResult<ActiveWindowInfo?>(_active);
        }

        public Task<IEnumerable<ActiveWindowInfo>> GetAllWindowsAsync()
        {
            return Task.FromResult<IEnumerable<ActiveWindowInfo>>(
            [
                _active,
                new ActiveWindowInfo
                {
                    Title = "Browser Docs",
                    ProcessName = "msedge",
                    ProcessId = 43,
                    WindowBounds = new Rectangle(0, 0, 800, 600),
                    IsVisible = true
                }
            ]);
        }
    }
}
