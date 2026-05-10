using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.AgentHost.ConsoleApp;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Commands;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Mcp;

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
        output.ToString().Should().Contain("/dependabot");
        output.ToString().Should().Contain("/github app");
        output.ToString().Should().Contain("/worktree");
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

    [Fact]
    public async Task RunAsync_TestSlashCommand_RunsDeterministicDotnetTest()
    {
        var runtime = new RecordingCommandRuntime(CommandRuntimeResult.Failed(
            "Should not run.",
            SkillExecutionStatus.Failed,
            "unexpected"));
        var processRunner = new RecordingProcessRunner(
            new CodingAgentProcessResult(0, "tests passed", string.Empty, false));
        var command = new CodingAgentCommand(runtime, processRunner);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/test --filter ignored",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        runtime.ReceivedCommand.Should().BeNull();
        processRunner.Requests.Should().ContainSingle();
        var request = processRunner.Requests.Single();
        request.FileName.Should().Be("dotnet");
        request.Arguments.Should().Equal(
            "test",
            "tests/SmartVoiceAgent.Tests/SmartVoiceAgent.Tests.csproj",
            "--configuration",
            "Release",
            "--verbosity",
            "normal");
        request.WorkingDirectory.Should().Be(_workspace);
        request.Timeout.Should().Be(TimeSpan.FromSeconds(900));
        output.ToString().Should().Contain("tests passed");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TestSlashCommand_ReturnsOneWhenTestsFail()
    {
        var processRunner = new RecordingProcessRunner(
            new CodingAgentProcessResult(1, "failed stdout", "failed stderr", false));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            processRunner);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/test",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("[FAIL] dotnet test");
        output.ToString().Should().Contain("failed stderr");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TestSlashCommand_ReturnsOneWhenProcessTimesOut()
    {
        var processRunner = new RecordingProcessRunner(
            new CodingAgentProcessResult(-1, string.Empty, string.Empty, true));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            processRunner);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/test",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("[TIMEOUT] dotnet test");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ReviewSlashCommand_RunsDeterministicGitChecks()
    {
        var processRunner = new RecordingProcessRunner(
            new CodingAgentProcessResult(0, "## main", string.Empty, false),
            new CodingAgentProcessResult(0, string.Empty, string.Empty, false),
            new CodingAgentProcessResult(0, string.Empty, string.Empty, false),
            new CodingAgentProcessResult(0, string.Empty, string.Empty, false),
            new CodingAgentProcessResult(0, string.Empty, string.Empty, false));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            processRunner);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/review",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        processRunner.Requests.Select(request => string.Join(' ', request.FileName, string.Join(' ', request.Arguments)))
            .Should()
            .Equal(
                "git status --short --branch",
                "git diff --check",
                "git diff --cached --check",
                "git diff --stat",
                "git diff --cached --stat");
        output.ToString().Should().Contain("No working tree diff to review.");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ReviewSlashCommand_ReturnsOneWhenWhitespaceCheckFails()
    {
        var processRunner = new RecordingProcessRunner(
            new CodingAgentProcessResult(0, "## main", string.Empty, false),
            new CodingAgentProcessResult(2, string.Empty, "trailing whitespace", false),
            new CodingAgentProcessResult(0, string.Empty, string.Empty, false),
            new CodingAgentProcessResult(0, string.Empty, string.Empty, false),
            new CodingAgentProcessResult(0, string.Empty, string.Empty, false));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            processRunner);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/review",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("trailing whitespace");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_DependabotSlashCommand_RunsDependencyAuditAndDependabotPrList()
    {
        var processRunner = new RecordingProcessRunner(
            new CodingAgentProcessResult(0, "no vulnerable packages", string.Empty, false),
            new CodingAgentProcessResult(0, "No pull requests match your search", string.Empty, false));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            processRunner);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/dependabot",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        processRunner.Requests.Should().HaveCount(2);
        processRunner.Requests[0].FileName.Should().Be("dotnet");
        processRunner.Requests[0].Arguments.Should().Contain("--vulnerable");
        processRunner.Requests[1].FileName.Should().Be("gh");
        processRunner.Requests[1].Arguments.Should().Contain("author:app/dependabot");
        output.ToString().Should().Contain("Dependabot/dependency audit");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_GithubSlashCommand_DegradesWhenGhIsUnavailable()
    {
        var processRunner = new RecordingProcessRunner(
            new CodingAgentProcessResult(0, "origin https://example.test/repo.git", string.Empty, false),
            new CodingAgentProcessResult(127, string.Empty, "gh not found", false),
            new CodingAgentProcessResult(127, string.Empty, "gh not found", false));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            processRunner);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/github",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("[DEGRADED] GitHub PR status");
        output.ToString().Should().Contain("gh not found");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_GithubAppSlashCommand_ShowsGitHubAppConnectionStatus()
    {
        var githubApp = new StaticGitHubAppClient(
            GitHubAppConnectionStatus.Connected(
                "12345",
                "98765",
                "https://api.github.com",
                "Kam Coding",
                "kam-coding",
                12),
            GitHubRepositoryListResult.Failed("not used", []));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            githubAppClient: githubApp);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/github app",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("Kam GitHub App:");
        output.ToString().Should().Contain("status: connected");
        output.ToString().Should().Contain("repositories: 12 accessible");
        output.ToString().Should().Contain("Metadata: read");
        output.ToString().Should().NotContain("installation-token");
        output.ToString().Should().NotContain("PRIVATE KEY");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_GithubReposSlashCommand_ListsGitHubAppAccessibleRepositories()
    {
        var githubApp = new StaticGitHubAppClient(
            GitHubAppConnectionStatus.Connected(
                "12345",
                "98765",
                "https://api.github.com",
                "Kam Coding",
                "kam-coding",
                2),
            GitHubRepositoryListResult.Succeeded(
                "2 repositories accessible.",
                [
                    new GitHubRepositorySummary(
                        "Esquetta/Kam",
                        true,
                        "master",
                        "https://github.com/Esquetta/Kam",
                        "https://github.com/Esquetta/Kam.git"),
                    new GitHubRepositorySummary(
                        "Esquetta/PublicTool",
                        false,
                        "main",
                        "https://github.com/Esquetta/PublicTool",
                        "https://github.com/Esquetta/PublicTool.git")
                ]));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            githubAppClient: githubApp);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/github repos",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("Kam GitHub App repositories:");
        output.ToString().Should().Contain("Esquetta/Kam");
        output.ToString().Should().Contain("private");
        output.ToString().Should().Contain("default: master");
        output.ToString().Should().Contain("Esquetta/PublicTool");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_GithubReposSlashCommand_WhenGitHubAppIsUnavailable_ShowsSetupGuidance()
    {
        var githubApp = new StaticGitHubAppClient(
            GitHubAppConnectionStatus.NotConfigured(
                "GitHub App is not configured.",
                ["GitHubApp:AppId", "GitHubApp:InstallationId", "GitHubApp:PrivateKeyPath"]),
            GitHubRepositoryListResult.Failed(
                "GitHub App is not configured.",
                ["GitHubApp:AppId", "GitHubApp:InstallationId", "GitHubApp:PrivateKeyPath"]));
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            githubAppClient: githubApp);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/github repos",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("GitHub App is not configured");
        output.ToString().Should().Contain("dotnet user-secrets set \"GitHubApp:AppId\"");
        output.ToString().Should().NotContain("PRIVATE KEY");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PluginsSlashCommand_ShowsSkillHealthSummary()
    {
        var healthService = new StaticSkillHealthService([
            new SkillHealthReport { SkillId = "files.read", Status = SkillHealthStatus.Healthy, Details = "ok" },
            new SkillHealthReport { SkillId = "shell.run", Status = SkillHealthStatus.PermissionDenied, Details = "missing shell" }
        ]);
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            skillHealthService: healthService);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/plugins",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("Healthy: 1");
        output.ToString().Should().Contain("PermissionDenied: 1");
        output.ToString().Should().Contain("shell.run");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_McpSlashCommand_RedactsSecretStatus()
    {
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")),
            mcpOptions: Options.Create(new McpOptions
            {
                TodoistServerLink = "https://todoist.example/mcp",
                TodoistApiKey = "secret-value"
            }));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/mcp",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("https://todoist.example/mcp");
        output.ToString().Should().Contain("Todoist API key: (configured)");
        output.ToString().Should().NotContain("secret-value");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WorktreeAdd_RequiresExplicitCreationFlag()
    {
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/worktree add ../kam-feature feature/test",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(2);
        output.ToString().Should().Contain("Worktree creation is intentionally not wired");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_AgentsSlashCommand_ShowsCodingRoleTemplates()
    {
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/agents",
                WorkspaceRoot = _workspace
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("coding role templates");
        output.ToString().Should().Contain("dependency-auditor");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_SummaryPath_WritesInsideWorkspace()
    {
        var summaryPath = Path.Combine("artifacts", "coding-agent", "summary.md");
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/help",
                WorkspaceRoot = _workspace,
                SummaryPath = summaryPath
            },
            output,
            error);

        exitCode.Should().Be(0);
        File.Exists(Path.Combine(_workspace, summaryPath)).Should().BeTrue();
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_SummaryPath_RejectsOutsideWorkspace()
    {
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/help",
                WorkspaceRoot = _workspace,
                SummaryPath = Path.Combine("..", "escape.md")
            },
            output,
            error);

        exitCode.Should().Be(2);
        error.ToString().Should().Contain("Summary path must stay inside the workspace");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_HooksSlashCommand_ShowsConfiguredHooks()
    {
        var command = new CodingAgentCommand(
            new RecordingCommandRuntime(CommandRuntimeResult.Failed("Should not run.", SkillExecutionStatus.Failed, "unexpected")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new CodingAgentCommandOptions
            {
                CommandText = "/hooks",
                WorkspaceRoot = _workspace,
                PreTestHooks = ["dotnet format --verify-no-changes"],
                PostReviewHooks = ["git status --short"]
            },
            output,
            error);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("dotnet format");
        output.ToString().Should().Contain("git status");
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

    private sealed class RecordingProcessRunner : ICodingAgentProcessRunner
    {
        private readonly Queue<CodingAgentProcessResult> _results;

        public RecordingProcessRunner(params CodingAgentProcessResult[] results)
        {
            _results = new Queue<CodingAgentProcessResult>(results);
        }

        public List<CodingAgentProcessRequest> Requests { get; } = [];

        public Task<CodingAgentProcessResult> RunAsync(
            CodingAgentProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(_results.Count == 0
                ? new CodingAgentProcessResult(0, string.Empty, string.Empty, false)
                : _results.Dequeue());
        }
    }

    private sealed class StaticSkillHealthService : ISkillHealthService
    {
        private readonly IReadOnlyCollection<SkillHealthReport> _reports;

        public StaticSkillHealthService(IReadOnlyCollection<SkillHealthReport> reports)
        {
            _reports = reports;
        }

        public Task<IReadOnlyCollection<SkillHealthReport>> GetHealthAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_reports);
        }
    }

    private sealed class StaticGitHubAppClient : IGitHubAppClient
    {
        private readonly GitHubAppConnectionStatus _status;
        private readonly GitHubRepositoryListResult _repositories;

        public StaticGitHubAppClient(
            GitHubAppConnectionStatus status,
            GitHubRepositoryListResult repositories)
        {
            _status = status;
            _repositories = repositories;
        }

        public Task<GitHubAppConnectionStatus> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_status);
        }

        public Task<GitHubRepositoryListResult> ListRepositoriesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_repositories);
        }
    }
}
