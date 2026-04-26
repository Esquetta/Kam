using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class ShellSkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ShellRun_ReturnsExitCodeAndOutput()
    {
        var executor = new ShellSkillExecutor();

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new
            {
                command = "echo kam-shell-test",
                timeoutMilliseconds = 5000,
                maxOutputLength = 2000
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Exit Code: 0");
        result.Message.Should().Contain("kam-shell-test");
    }

    [Fact]
    public async Task ExecuteAsync_ShellRun_BlocksDangerousCommands()
    {
        var executor = new ShellSkillExecutor();

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new { command = "git reset --hard" }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("blocked");
    }
}
