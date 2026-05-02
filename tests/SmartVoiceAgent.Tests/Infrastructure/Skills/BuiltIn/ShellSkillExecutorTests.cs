using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;
using SmartVoiceAgent.Infrastructure.Skills.Policy;
using System.Runtime.InteropServices;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class ShellSkillExecutorTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _otherWorkspace;

    public ShellSkillExecutorTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-shell-tests-{id}");
        _otherWorkspace = Path.Combine(Path.GetTempPath(), $"kam-shell-tests-other-{id}");
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_otherWorkspace);
    }

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
    public async Task ExecuteAsync_ShellRun_ReturnsStructuredExecutionData()
    {
        var executor = new ShellSkillExecutor();

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new
            {
                command = EchoStdOutCommand("kam-structured-output"),
                workingDirectory = _workspace,
                timeoutMilliseconds = 5000,
                maxOutputLength = 2000
            }));

        result.Success.Should().BeTrue();
        var data = result.Data.Should().BeOfType<ShellCommandResult>().Subject;
        data.Command.Should().Be(EchoStdOutCommand("kam-structured-output"));
        data.WorkingDirectory.Should().Be(Path.GetFullPath(_workspace));
        data.ExitCode.Should().Be(0);
        data.StdOut.Should().Contain("kam-structured-output");
        data.StdErr.Should().BeEmpty();
        data.TimedOut.Should().BeFalse();
        data.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShellRun_SeparatesStdoutAndStderr()
    {
        var executor = new ShellSkillExecutor();

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new
            {
                command = StdOutAndStdErrCommand(),
                workingDirectory = _workspace,
                timeoutMilliseconds = 5000,
                maxOutputLength = 2000
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Stdout:");
        result.Message.Should().Contain("Stderr:");
        var data = result.Data.Should().BeOfType<ShellCommandResult>().Subject;
        data.StdOut.Should().Contain("kam-stdout");
        data.StdErr.Should().Contain("kam-stderr");
    }

    [Fact]
    public async Task ExecuteAsync_ShellRun_TimesOutWithStructuredResult()
    {
        var executor = new ShellSkillExecutor();

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new
            {
                command = SleepCommand(),
                workingDirectory = _workspace,
                timeoutMilliseconds = 1000,
                maxOutputLength = 2000
            }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.TimedOut);
        result.ErrorCode.Should().Be("timeout");
        var data = result.Data.Should().BeOfType<ShellCommandResult>().Subject;
        data.TimedOut.Should().BeTrue();
        data.ExitCode.Should().BeNull();
        data.WorkingDirectory.Should().Be(Path.GetFullPath(_workspace));
    }

    [Fact]
    public async Task ExecuteAsync_ShellRun_TruncatesStructuredOutput()
    {
        var executor = new ShellSkillExecutor();

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new
            {
                command = LongStdOutCommand(new string('x', 700)),
                workingDirectory = _workspace,
                timeoutMilliseconds = 5000,
                maxOutputLength = 500
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("[truncated]");
        var data = result.Data.Should().BeOfType<ShellCommandResult>().Subject;
        data.Truncated.Should().BeTrue();
        (data.StdOut.Length + data.StdErr.Length).Should().BeLessThanOrEqualTo(500);
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
    public async Task ExecuteAsync_ShellRun_BlocksDeleteCommandBeforeItTouchesDisk()
    {
        var filePath = Path.Combine(_workspace, "keep-me.txt");
        await File.WriteAllTextAsync(filePath, "do not delete");
        var executor = new ShellSkillExecutor();

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new { command = DeleteFileCommand(filePath), workingDirectory = _workspace }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.PermissionDenied);
        result.ErrorCode.Should().Be("shell_command_blocked");
        File.Exists(filePath).Should().BeTrue("destructive shell deletes must be blocked before execution");
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

    [Fact]
    public async Task ExecuteAsync_ShellRun_BlocksWorkingDirectoryOutsideConfiguredAllowedRoots()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "shell.run",
            RuntimeOptions = new Dictionary<string, string>
            {
                [SkillRuntimePolicyOptions.ShellAllowedWorkingDirectories] = _workspace
            }
        });
        var executor = new ShellSkillExecutor(registry);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new
            {
                command = EchoStdOutCommand("blocked-working-directory"),
                workingDirectory = _otherWorkspace
            }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.PermissionDenied);
        result.ErrorCode.Should().Be("shell_working_directory_not_allowed");
    }

    [Fact]
    public async Task ExecuteAsync_ShellRun_AllowsWorkingDirectoryInsideConfiguredAllowedRoot()
    {
        var childDirectory = Path.Combine(_workspace, "child");
        Directory.CreateDirectory(childDirectory);
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "shell.run",
            RuntimeOptions = new Dictionary<string, string>
            {
                [SkillRuntimePolicyOptions.ShellAllowedWorkingDirectories] = _workspace
            }
        });
        var executor = new ShellSkillExecutor(registry);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "shell.run",
            new
            {
                command = EchoStdOutCommand("allowed-working-directory"),
                workingDirectory = childDirectory
            }));

        result.Success.Should().BeTrue();
        var data = result.Data.Should().BeOfType<ShellCommandResult>().Subject;
        data.WorkingDirectory.Should().Be(Path.GetFullPath(childDirectory));
    }

    public void Dispose()
    {
        TryDeleteDirectory(_workspace);
        TryDeleteDirectory(_otherWorkspace);
    }

    private static string EchoStdOutCommand(string value)
    {
        return IsWindows()
            ? $"Write-Output '{value}'"
            : $"printf '{value}\\n'";
    }

    private static string StdOutAndStdErrCommand()
    {
        return IsWindows()
            ? "Write-Output 'kam-stdout'; [Console]::Error.WriteLine('kam-stderr')"
            : "printf 'kam-stdout\\n'; printf 'kam-stderr\\n' 1>&2";
    }

    private static string LongStdOutCommand(string value)
    {
        return IsWindows()
            ? $"Write-Output '{value}'"
            : $"printf '{value}'";
    }

    private static string SleepCommand()
    {
        return IsWindows()
            ? "Start-Sleep -Seconds 2"
            : "sleep 2";
    }

    private static string DeleteFileCommand(string filePath)
    {
        return IsWindows()
            ? $"cmd /c del /q \"{filePath}\""
            : $"rm \"{filePath}\"";
    }

    private static bool IsWindows()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Test cleanup should not hide assertion failures.
        }
    }
}
