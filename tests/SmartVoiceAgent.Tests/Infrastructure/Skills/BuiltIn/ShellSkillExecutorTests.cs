using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
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

    [Fact]
    public async Task ExecuteAsync_ShellRun_BlocksRuntimePolicyPatterns()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "shell.run",
            RuntimeOptions = new Dictionary<string, string>
            {
                ["shell.blockedPatterns"] = "kam-blocked-token"
            }
        });
        var executor = new ShellSkillExecutor(registry);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new { command = "echo kam-blocked-token" }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.PermissionDenied);
        result.ErrorCode.Should().Be("shell_command_blocked");
    }

    [Fact]
    public async Task ExecuteAsync_ShellRun_RequiresConfiguredAllowedCommandPrefix()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "shell.run",
            RuntimeOptions = new Dictionary<string, string>
            {
                ["shell.allowedCommands"] = "dotnet;git status"
            }
        });
        var executor = new ShellSkillExecutor(registry);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new { command = "powershell -Command Get-Process" }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.PermissionDenied);
        result.ErrorCode.Should().Be("shell_command_not_allowed");
    }
}
