using FluentAssertions;
using SmartVoiceAgent.AgentHost.ConsoleApp;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Commands;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Tests.AgentHost;

public sealed class CommandSmokeCommandTests
{
    [Fact]
    public async Task RunAsync_SuccessfulCommand_WritesSummaryAndReturnsSuccess()
    {
        var summaryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "command-smoke.md");
        var skillResult = SkillResult.Succeeded("Found 12 applications.") with
        {
            DurationMilliseconds = 42
        };
        var runtime = new RecordingCommandRuntime(CommandRuntimeResult.Succeeded(
            "Found 12 applications.",
            "apps.list",
            skillResult));
        var command = new CommandSmokeCommand(runtime);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CommandSmokeOptions
            {
                CommandText = "list applications",
                SummaryPath = summaryPath
            },
            output,
            error);

        exitCode.Should().Be(0);
        runtime.ReceivedCommand.Should().Be("list applications");
        File.Exists(summaryPath).Should().BeTrue();
        var markdown = await File.ReadAllTextAsync(summaryPath);
        markdown.Should().Contain("# Command Smoke");
        markdown.Should().Contain("- status: completed");
        markdown.Should().Contain("- command: list applications");
        markdown.Should().Contain("- skillId: apps.list");
        markdown.Should().Contain("- success: True");
        output.ToString().Should().Contain("Command smoke completed.");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ConfirmationRequired_ReturnsError()
    {
        var command = new CommandSmokeCommand(new RecordingCommandRuntime(
            CommandRuntimeResult.PendingConfirmation(
                "Skill 'apps.open' requires confirmation before execution.",
                "apps.open",
                Guid.NewGuid())));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CommandSmokeOptions { CommandText = "open spotify" },
            output,
            error);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("[FAIL] command smoke");
        error.ToString().Should().Contain("requires confirmation");
    }

    [Fact]
    public void ParseOptions_UsesAliasesAndDefaultCommand()
    {
        var defaultOptions = CommandSmokeCommand.ParseOptions([CommandSmokeCommand.SwitchName]);
        var customOptions = CommandSmokeCommand.ParseOptions(
            [CommandSmokeCommand.SwitchName, "--command-smoke-command", "list installed apps", "--command-smoke-summary", "out.md"]);

        defaultOptions.CommandText.Should().Be(CommandSmokeCommand.DefaultCommandText);
        customOptions.CommandText.Should().Be("list installed apps");
        customOptions.SummaryPath.Should().Be("out.md");
    }

    private sealed class RecordingCommandRuntime : ICommandRuntimeService
    {
        private readonly CommandRuntimeResult _result;

        public RecordingCommandRuntime(CommandRuntimeResult result)
        {
            _result = result;
        }

        public string? ReceivedCommand { get; private set; }

        public Task<CommandRuntimeResult> ExecuteAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            ReceivedCommand = command;
            return Task.FromResult(_result);
        }
    }
}
