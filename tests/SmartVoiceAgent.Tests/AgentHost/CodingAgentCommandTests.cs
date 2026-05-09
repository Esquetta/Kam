using System.Diagnostics;
using FluentAssertions;
using SmartVoiceAgent.AgentHost.ConsoleApp;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Commands;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Tests.AgentHost;

public sealed class CodingAgentCommandTests : IDisposable
{
    private readonly string _workspace;

    public CodingAgentCommandTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-coding-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public void ParseOptions_ConfiguresWorkspaceCommandAndApprovalMode()
    {
        var options = CodingAgentCommand.ParseOptions(
            [
                CodingAgentCommand.SwitchName,
                "--workspace",
                _workspace,
                "--approval-mode",
                "read-only",
                "--command",
                "/permissions"
            ]);

        options.WorkspaceRoot.Should().Be(Path.GetFullPath(_workspace));
        options.ApprovalMode.Should().Be("read-only");
        options.CommandText.Should().Be("/permissions");
    }

    [Fact]
    public void CreateConfigurationOverrides_EnablesCodingAgentPolicy()
    {
        var options = new CodingAgentCommandOptions
        {
            WorkspaceRoot = _workspace,
            ApprovalMode = "workspace-write"
        };

        var overrides = CodingAgentCommand.CreateConfigurationOverrides(options);

        overrides.Should().Contain("CodingAgent:IsEnabled", "true");
        overrides.Should().Contain("CodingAgent:WorkspaceRoot", _workspace);
        overrides.Should().Contain("CodingAgent:ApprovalMode", "workspace-write");
        overrides.Should().Contain("CodingAgent:RequireShellAllowList", "true");
    }

    [Fact]
    public async Task RunAsync_HelpSlashCommand_DoesNotInvokeRuntime()
    {
        var runtime = new RecordingCommandRuntime(CommandRuntimeResult.Failed(
            "Should not run.",
            SkillExecutionStatus.Failed,
            "unexpected"));
        var command = new CodingAgentCommand(runtime);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/help",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        runtime.ReceivedCommand.Should().BeNull();
        output.ToString().Should().Contain("/permissions");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PermissionsSlashCommand_ShowsWorkspacePolicy()
    {
        var command = new CodingAgentCommand(new RecordingCommandRuntime(
            CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/permissions",
                WorkspaceRoot = _workspace,
                ApprovalMode = "workspace-write"
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain(Path.GetFullPath(_workspace));
        output.ToString().Should().Contain("shellAllowList: required");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_DiffSlashCommand_ReportsStagedDiff()
    {
        await RunGitAsync("init");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "notes.md"), "# Kam");
        await RunGitAsync("add notes.md");

        var command = new CodingAgentCommand(new RecordingCommandRuntime(
            CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/diff",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("notes.md");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PlainTextCommand_DelegatesToCommandRuntime()
    {
        var skillResult = SkillResult.Succeeded("Listed applications.") with
        {
            DurationMilliseconds = 12
        };
        var runtime = new RecordingCommandRuntime(CommandRuntimeResult.Succeeded(
            "Listed applications.",
            "apps.list",
            skillResult));
        var command = new CodingAgentCommand(runtime);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "list applications",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        runtime.ReceivedCommand.Should().Be("list applications");
        output.ToString().Should().Contain("[PASS] coding-agent");
        error.ToString().Should().BeEmpty();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }
        }
        catch
        {
            // Cleanup must not hide assertion failures.
        }
    }

    private async Task RunGitAsync(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _workspace,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start().Should().BeTrue();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0);
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
