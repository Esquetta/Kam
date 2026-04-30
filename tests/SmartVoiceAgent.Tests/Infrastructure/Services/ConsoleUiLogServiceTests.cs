using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.UiLog;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class ConsoleUiLogServiceTests
{
    [Fact]
    public void Log_RaisesLogEntry()
    {
        var service = new ConsoleUiLogService();
        UiLogEntry? entry = null;
        service.OnLogEntry += (_, received) => entry = received;

        service.Log("kernel ready", LogLevel.Information, "Test");

        entry.Should().NotBeNull();
        entry!.Message.Should().Be("kernel ready");
        entry.Source.Should().Be("Test");
        entry.Level.Should().Be(LogLevel.Information);
        entry.IsAgentUpdate.Should().BeFalse();
    }

    [Fact]
    public void LogAgentUpdate_RaisesAgentLogEntry()
    {
        var service = new ConsoleUiLogService();
        UiLogEntry? entry = null;
        service.OnLogEntry += (_, received) => entry = received;

        service.LogAgentUpdate("planner", "done", isComplete: true);

        entry.Should().NotBeNull();
        entry!.AgentName.Should().Be("planner");
        entry.Message.Should().Be("done");
        entry.IsAgentUpdate.Should().BeTrue();
        entry.IsComplete.Should().BeTrue();
    }
}
