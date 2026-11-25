using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.CliAgents;
using SmartVoiceAgent.Infrastructure.Agent.CliAgents;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.CliAgents;

public sealed class CodexCliAgentTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("kam-codex-tests").FullName;

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    [Fact]
    public void BuildArguments_ReadOnlyByDefaultAndTaskOnStandardInput()
    {
        var arguments = CodexCliAgent.BuildArguments(new CliAgentRunRequest("Explain Program.cs", _workspace, false, TimeSpan.FromMinutes(5)));

        arguments.Should().Equal("exec", "--json", "--skip-git-repo-check", "--sandbox", "read-only", "--cd", _workspace, "-");
        arguments.Should().NotContain(argument => argument.Contains("dangerously", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildArguments_AllowEdits_UsesWorkspaceWriteSandbox()
    {
        var arguments = CodexCliAgent.BuildArguments(new CliAgentRunRequest("Fix the bug", _workspace, true, TimeSpan.FromMinutes(5)));

        arguments.Should().ContainInOrder("--sandbox", "workspace-write");
        arguments.Should().NotContain("--skip-git-repo-check", "edits stay inside a git repository, where they can be undone");
    }

    [Fact]
    public async Task RunAsync_StartsTheCliInTheWorkspaceWithTheTaskOnStandardInput()
    {
        var runner = new FakeProcessRunner(_ => new CodingAgentProcessResult(0, SuccessfulRun(_workspace), string.Empty, false));
        var agent = new CodexCliAgent(runner);
        var launch = new CliLaunch("node", ["C:/npm/codex.js"]);

        var result = await agent.RunAsync(
            launch,
            new CliAgentRunRequest("Explain \"Program.cs\" & exit", _workspace, false, TimeSpan.FromMinutes(7)),
            TestContext.Current.CancellationToken);

        var request = runner.Requests.Should().ContainSingle().Subject;
        request.FileName.Should().Be("node");
        request.Arguments[0].Should().Be("C:/npm/codex.js");
        request.Arguments.Skip(1).Should().StartWith(["exec", "--json"]);
        request.Arguments.Should().NotContain("Explain \"Program.cs\" & exit");
        request.StandardInput.Should().Be("Explain \"Program.cs\" & exit");
        request.WorkingDirectory.Should().Be(_workspace);
        request.Timeout.Should().Be(TimeSpan.FromMinutes(7));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void ParseEvents_KeepsFinalAnswerChangedFilesCommandsAndUsage()
    {
        var result = CodexCliAgent.ParseEvents(
            new CodingAgentProcessResult(0, SuccessfulRun(_workspace), string.Empty, false),
            _workspace,
            TimeSpan.FromMinutes(20));

        result.Success.Should().BeTrue();
        result.FinalMessage.Should().Be("Renamed the helper and updated its caller.");
        result.ChangedFiles.Should().Equal("update src/Helper.cs", "add docs/notes.md");
        result.Commands.Should().Equal("dotnet build (exit 0)");
        result.Usage.Should().Contain("1,200 input tokens").And.Contain("80 output tokens");
        result.Error.Should().BeNull();

        var text = result.ToToolResult(CodexCliAgent.DisplayName);
        text.Should().StartWith("Codex CLI finished.");
        text.Should().Contain("Final answer:").And.Contain("- update src/Helper.cs").And.Contain("Commands run:");
    }

    [Fact]
    public void ParseEvents_TurnFailed_ReportsTheCliError()
    {
        const string output = """
            {"type":"thread.started","thread_id":"t1"}
            {"type":"turn.started"}
            {"type":"turn.failed","error":{"message":"You've hit your usage limit."}}
            """;

        var result = CodexCliAgent.ParseEvents(new CodingAgentProcessResult(1, output, "ignored", false), _workspace, TimeSpan.FromMinutes(20));

        result.Success.Should().BeFalse();
        result.Error.Should().Be("You've hit your usage limit.");
        result.ToToolResult(CodexCliAgent.DisplayName).Should().StartWith("Error: Codex CLI did not finish: You've hit your usage limit.");
    }

    [Fact]
    public void ParseEvents_NonZeroExitWithoutEvents_UsesStandardError()
    {
        var result = CodexCliAgent.ParseEvents(
            new CodingAgentProcessResult(2, "not json", "error: unexpected argument '--foo'", false),
            _workspace,
            TimeSpan.FromMinutes(20));

        result.Success.Should().BeFalse();
        result.Error.Should().Be("exit code 2. error: unexpected argument '--foo'");
    }

    [Fact]
    public void ParseEvents_TimedOut_NamesTheLimitAndKeepsPartialWork()
    {
        var partial = """{"type":"item.completed","item":{"id":"i1","type":"agent_message","text":"Halfway there."}}""";

        var result = CodexCliAgent.ParseEvents(new CodingAgentProcessResult(-1, partial, string.Empty, true), _workspace, TimeSpan.FromMinutes(20));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("20 minutes");
        result.FinalMessage.Should().Be("Halfway there.");
    }

    private static string SuccessfulRun(string workspace)
    {
        var helper = Path.Combine(workspace, "src", "Helper.cs").Replace("\\", "\\\\");
        return $$$"""
            {"type":"thread.started","thread_id":"0199a213-81c0-7800-8aa1-bbab2a035a53"}
            {"type":"turn.started"}
            {"type":"item.completed","item":{"id":"item_0","type":"reasoning","text":"Looking at the helper."}}
            {"type":"item.started","item":{"id":"item_1","type":"command_execution","command":"dotnet build","aggregated_output":"","exit_code":null,"status":"in_progress"}}
            {"type":"item.completed","item":{"id":"item_1","type":"command_execution","command":"dotnet build","aggregated_output":"Build succeeded.","exit_code":0,"status":"completed"}}
            {"type":"item.completed","item":{"id":"item_2","type":"file_change","changes":[{"path":"{{{helper}}}","kind":"update"},{"path":"docs/notes.md","kind":"add"}],"status":"completed"}}
            {"type":"item.completed","item":{"id":"item_3","type":"agent_message","text":"Renamed the helper and updated its caller."}}
            {"type":"turn.completed","usage":{"input_tokens":1200,"cached_input_tokens":1000,"output_tokens":80}}
            """;
    }
}

public sealed class CliExecutableLocatorTests : IDisposable
{
    private readonly string _bin = Directory.CreateTempSubdirectory("kam-locator-tests").FullName;

    public void Dispose() => Directory.Delete(_bin, recursive: true);

    [Fact]
    public void Find_WindowsExe_StartsItDirectly()
    {
        File.WriteAllText(Path.Combine(_bin, "claude.exe"), string.Empty);

        var launch = CliExecutableLocator.Find("claude", $"C:\\missing;{_bin}", isWindows: true);

        launch.Should().NotBeNull();
        launch!.FileName.Should().Be(Path.Combine(_bin, "claude.exe"));
        launch.PrefixArguments.Should().BeEmpty();
    }

    [Fact]
    public void Find_NpmShim_StartsNodeWithThePackageScript()
    {
        var script = Path.Combine(_bin, "node_modules", "@openai", "codex", "bin", "codex.js");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, "// codex");
        File.WriteAllText(
            Path.Combine(_bin, "codex.cmd"),
            "@ECHO off\r\nendLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & \"%_prog%\"  \"%dp0%\\node_modules\\@openai\\codex\\bin\\codex.js\" %*\r\n");

        var nodeBin = Path.Combine(_bin, "nodejs");
        Directory.CreateDirectory(nodeBin);
        File.WriteAllText(Path.Combine(nodeBin, "node.exe"), string.Empty);

        var launch = CliExecutableLocator.Find("codex", $"{_bin};{nodeBin}", isWindows: true);

        launch.Should().NotBeNull();
        launch!.FileName.Should().Be(Path.Combine(nodeBin, "node.exe"), "a bare \"node\" would be found through CreateProcess's search order");
        launch.PrefixArguments.Should().Equal(Path.GetFullPath(script));
        launch.WithArguments("--version").Should().Equal(Path.GetFullPath(script), "--version");

        CliExecutableLocator.Find("codex", _bin, isWindows: true).Should().BeNull("there is no node.exe on that PATH");
    }

    [Fact]
    public void Find_BatchFileThatIsNotAnNpmShim_IsNotUsed()
    {
        File.WriteAllText(Path.Combine(_bin, "codex.cmd"), "@echo off\r\nsomething-else.exe %*\r\n");

        CliExecutableLocator.Find("codex", _bin, isWindows: true).Should().BeNull();
    }

    [Fact]
    public void Find_Posix_UsesTheBareName()
    {
        // A POSIX PATH is colon-separated, so a Windows temp folder (C:\...) cannot be listed in one.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.WriteAllText(Path.Combine(_bin, "codex"), string.Empty);

        CliExecutableLocator.Find("codex", $"/missing:{_bin}", isWindows: false)!.FileName.Should().Be(Path.Combine(_bin, "codex"));
        CliExecutableLocator.Find("claude", _bin, isWindows: false).Should().BeNull();
    }
}

public sealed class CliAgentDetectorTests
{
    [Fact]
    public async Task DetectAsync_ReportsVersionsAndSignInWithoutAccountDetails()
    {
        var runner = new FakeProcessRunner(request => string.Join(' ', request.Arguments) switch
        {
            "--version" when request.FileName == "codex" => new CodingAgentProcessResult(0, "codex-cli 0.161.0\n", string.Empty, false),
            "login status" => new CodingAgentProcessResult(0, string.Empty, "Logged in using ChatGPT\n", false),
            "--version" => new CodingAgentProcessResult(0, "2.1.296 (Claude Code)\n", string.Empty, false),
            "auth status --json" => new CodingAgentProcessResult(
                0,
                """{"loggedIn":true,"authMethod":"claude.ai","email":"someone@example.com","orgName":"Example Org","subscriptionType":"max"}""",
                string.Empty,
                false),
            var other => throw new InvalidOperationException(other)
        });
        var detector = new CliAgentDetector(runner, name => new CliLaunch(name, []));

        var statuses = await detector.DetectAsync(TestContext.Current.CancellationToken);

        statuses.Should().BeEquivalentTo(
        [
            new CliAgentStatus(CliAgentKind.Codex, true, "0.161.0", CliAgentSignIn.Subscription),
            new CliAgentStatus(CliAgentKind.ClaudeCode, true, "2.1.296", CliAgentSignIn.Subscription, "max")
        ]);
        statuses.Select(status => status.ToString()).Should().NotContain(text => text.Contains("example", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectAsync_CliNotOnPath_RunsNothing()
    {
        var runner = new FakeProcessRunner(_ => throw new InvalidOperationException("should not run"));
        var detector = new CliAgentDetector(runner, _ => null);

        var statuses = await detector.DetectAsync(TestContext.Current.CancellationToken);

        statuses.Should().OnlyContain(status => !status.IsInstalled && status.Version == null);
        runner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, "Logged in using ChatGPT", CliAgentSignIn.Subscription)]
    [InlineData(0, "Logged in using an API key - sk-proj-***ABCD", CliAgentSignIn.ApiKey)]
    [InlineData(1, "Not logged in", CliAgentSignIn.SignedOut)]
    [InlineData(127, "", CliAgentSignIn.Unknown)]
    public void ParseCodexLogin_MapsTheCliMessage(int exitCode, string output, CliAgentSignIn expected)
    {
        CliAgentDetector.ParseCodexLogin(new CodingAgentProcessResult(exitCode, string.Empty, output, false)).SignIn.Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"loggedIn":false}""", CliAgentSignIn.SignedOut)]
    [InlineData("""{"loggedIn":true,"authMethod":"api_key"}""", CliAgentSignIn.ApiKey)]
    [InlineData("not json", CliAgentSignIn.Unknown)]
    public void ParseClaudeAuth_MapsTheCliJson(string output, CliAgentSignIn expected)
    {
        CliAgentDetector.ParseClaudeAuth(new CodingAgentProcessResult(0, output, string.Empty, false)).SignIn.Should().Be(expected);
    }
}

public sealed class CliAgentToolProviderTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("kam-cli-tool-tests").FullName;

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    [Fact]
    public async Task GetToolsAsync_CodexSwitchedOff_OffersNothing()
    {
        var provider = Provider(new CliAgentsOptions { CodexEnabled = false }, new FakeProcessRunner(_ => Ok()));

        (await provider.GetToolsAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetToolsAsync_CodexNotInstalled_OffersNothing()
    {
        var provider = new CliAgentToolProvider(
            new StaticOptionsMonitor<CliAgentsOptions>(new CliAgentsOptions { CodexEnabled = true }),
            new FakeProcessRunner(_ => Ok()),
            locate: _ => null);

        (await provider.GetToolsAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetToolsAsync_CodexOn_OffersOneDelegateTool()
    {
        var provider = Provider(new CliAgentsOptions { CodexEnabled = true }, new FakeProcessRunner(_ => Ok()));

        var tool = (await provider.GetToolsAsync(TestContext.Current.CancellationToken)).Should().ContainSingle().Subject;

        tool.Name.Should().Be(CliAgentToolProvider.ToolName);
        tool.Risk.Should().Be(ToolRisk.Delegate);
        tool.Function.JsonSchema.GetRawText().Should().Contain("\"allow_edits\"").And.Contain("\"codex\"");
    }

    [Fact]
    public async Task Invoke_NoWorkspaceGiven_UsesTheCodingWorkspaceAndPassesAllowEdits()
    {
        var runner = new FakeProcessRunner(_ => Ok());
        var provider = Provider(new CliAgentsOptions { CodexEnabled = true, TimeoutMinutes = 3 }, runner);

        var result = await InvokeAsync(provider, ("agent", "codex"), ("task", "Fix the failing test"), ("allow_edits", true));

        result.Should().StartWith("Codex CLI finished.");
        var request = runner.Requests.Should().ContainSingle().Subject;
        request.WorkingDirectory.Should().Be(_workspace);
        request.Arguments.Should().ContainInOrder("--sandbox", "workspace-write", "--cd", _workspace);
        request.Timeout.Should().Be(TimeSpan.FromMinutes(3));
        request.StandardInput.Should().Be("Fix the failing test");
    }

    [Theory]
    [InlineData("claude", "relative", "is not available")]
    [InlineData("codex", "relative/folder", "full path")]
    [InlineData("codex", "C:/does/not/exist/kam", "does not exist")]
    public async Task Invoke_RejectsBadArgumentsWithoutStartingTheCli(string agent, string workspace, string error)
    {
        var runner = new FakeProcessRunner(_ => Ok());
        var provider = Provider(new CliAgentsOptions { CodexEnabled = true }, runner);
        if (!OperatingSystem.IsWindows() && workspace.StartsWith("C:", StringComparison.Ordinal))
        {
            workspace = "/does/not/exist/kam";
        }

        var result = await InvokeAsync(provider, ("agent", agent), ("task", "Explain"), ("workspace", workspace));

        result.Should().StartWith("Error:").And.Contain(error);
        runner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareArguments_FillsTheFolderAndSandboxBeforeApproval()
    {
        var provider = Provider(new CliAgentsOptions { CodexEnabled = true }, new FakeProcessRunner(_ => Ok()));
        var tool = (await provider.GetToolsAsync(TestContext.Current.CancellationToken)).Single();

        var prepared = tool.PrepareArguments!(new Dictionary<string, object?> { ["agent"] = "codex", ["task"] = "Explain" });

        prepared["workspace"].Should().Be(_workspace);
        prepared["allow_edits"].Should().Be(false);
        prepared["sandbox"].Should().Be("read-only");
        prepared["task"].Should().Be("Explain");
        provider.PrepareArguments(new Dictionary<string, object?> { ["allow_edits"] = true })["sandbox"].Should().Be("workspace-write");
    }

    [Theory]
    [InlineData(@"\\server\share\repo", "network or device path")]
    [InlineData(@"\\?\C:\repo", "network or device path")]
    [InlineData(@"C:\", "whole drive")]
    public void RejectWorkspace_RefusesNetworkPathsAndDrives(string workspace, string reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CliAgentToolProvider.RejectWorkspace(workspace).Should().Contain(reason);
    }

    [Fact]
    public void RejectWorkspace_RefusesSystemFoldersAndAllowsProjects()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0)
        {
            CliAgentToolProvider.RejectWorkspace(Path.Combine(windows, "System32")).Should().Contain("system folder");
        }

        CliAgentToolProvider.RejectWorkspace(_workspace).Should().BeNull();
    }

    private CliAgentToolProvider Provider(CliAgentsOptions options, FakeProcessRunner runner) =>
        new(new StaticOptionsMonitor<CliAgentsOptions>(options), runner, () => _workspace, name => new CliLaunch(name, []));

    private static async Task<string> InvokeAsync(CliAgentToolProvider provider, params (string Key, object? Value)[] arguments)
    {
        var tool = (await provider.GetToolsAsync(TestContext.Current.CancellationToken)).Single();
        var result = await tool.Function.InvokeAsync(
            new AIFunctionArguments(arguments.ToDictionary(argument => argument.Key, argument => argument.Value)),
            TestContext.Current.CancellationToken);
        return result?.ToString() ?? string.Empty;
    }

    private static CodingAgentProcessResult Ok() => new(
        0,
        """{"type":"item.completed","item":{"id":"i1","type":"agent_message","text":"Done."}}""",
        string.Empty,
        false);
}

public sealed class CodingAgentProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_WritesStandardInputAsUtf8()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await new CodingAgentProcessRunner().RunAsync(
            new CodingAgentProcessRequest("findstr.exe", ["/R", "^"], Path.GetTempPath(), TimeSpan.FromSeconds(30), "satır bir\nline two\n"),
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("satır bir").And.Contain("line two");
    }

    [Fact]
    public async Task RunAsync_ChildKeepsTheOutputPipeOpen_ReturnsAfterTheProcessExits()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // cmd exits at once, but "start /b" leaves ping running with cmd's stdout, so the pipe stays open for ~20 s.
        var started = DateTime.UtcNow;

        var result = await new CodingAgentProcessRunner().RunAsync(
            new CodingAgentProcessRequest("cmd.exe", ["/c", "start", "/b", "ping", "-n", "20", "127.0.0.1"], Path.GetTempPath(), TimeSpan.FromMinutes(1)),
            TestContext.Current.CancellationToken);

        result.TimedOut.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunAsync_Cancelled_KillsTheProcessAndThrows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;

        var run = () => new CodingAgentProcessRunner().RunAsync(
            new CodingAgentProcessRequest("ping.exe", ["-n", "30", "127.0.0.1"], Path.GetTempPath(), TimeSpan.FromMinutes(1)),
            cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(10));
    }
}

internal sealed class FakeProcessRunner(Func<CodingAgentProcessRequest, CodingAgentProcessResult> respond) : ICodingAgentProcessRunner
{
    private readonly object _gate = new();

    public List<CodingAgentProcessRequest> Requests { get; } = [];

    public Task<CodingAgentProcessResult> RunAsync(CodingAgentProcessRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Requests.Add(request);
        }

        return Task.FromResult(respond(request));
    }
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
