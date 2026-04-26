using System.Drawing;
using FluentAssertions;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Dtos.Screen;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class AccessibilitySkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AccessibilityTree_ReturnsScreenTextAndObjects()
    {
        var executor = new AccessibilitySkillExecutor(
            new FakeScreenContextService(),
            new FakeActiveWindowService());

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "accessibility.tree",
            new { maxNodes = 4 }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Accessibility Tree");
        result.Message.Should().Contain("Kam Editor");
        result.Message.Should().Contain("Run Skill");
        result.Message.Should().Contain("button");
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
                    DeviceName = "DISPLAY1",
                    IsPrimary = true,
                    Width = 1920,
                    Height = 1080,
                    ScreenshotHash = "abc",
                    Timestamp = DateTimeOffset.UtcNow,
                    ActiveWindow = new ActiveWindowInfo
                    {
                        Title = "Kam Editor",
                        ProcessName = "Code",
                        ProcessId = 42,
                        WindowBounds = new Rectangle(10, 20, 1200, 800),
                        IsVisible = true,
                        HasFocus = true
                    },
                    OcrLines =
                    [
                        new OcrLine(1, "Run Skill", 0.95, new Rectangle(40, 40, 120, 32)),
                        new OcrLine(2, "Skill Health", 0.90, new Rectangle(40, 80, 160, 32))
                    ],
                    Objects =
                    [
                        new ObjectDetectionItem
                        {
                            Label = "button",
                            Confidence = 0.88f,
                            BoundingBox = new Rectangle(40, 40, 120, 32)
                        }
                    ]
                }
            ]);
        }
    }

    private sealed class FakeActiveWindowService : IActiveWindowService
    {
        public Task<ActiveWindowInfo> GetActiveWindowInfoAsync()
        {
            return Task.FromResult(new ActiveWindowInfo
            {
                Title = "Kam Editor",
                ProcessName = "Code",
                ProcessId = 42,
                WindowBounds = new Rectangle(10, 20, 1200, 800),
                IsVisible = true,
                HasFocus = true
            });
        }

        public Task<IEnumerable<ActiveWindowInfo>> GetAllWindowsAsync()
        {
            return Task.FromResult<IEnumerable<ActiveWindowInfo>>([]);
        }
    }
}
