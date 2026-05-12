using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.CodingAgent;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Models.Updates;
using SmartVoiceAgent.Infrastructure.Agent.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Mcp;
using SmartVoiceAgent.Infrastructure.Services;
using System.Security.Cryptography;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class SlashCommandServiceTests
{
    [Fact]
    public void GetSuggestions_WhenSlashPrefixIsTyped_IncludesCodingWorkflowCommands()
    {
        var service = new SlashCommandService();

        var suggestions = service.GetSuggestions("/");

        suggestions.Select(command => command.Name)
            .Should()
            .Contain([
                "/dependabot",
                "/diff",
                "/github",
                "/github app",
                "/github app actions",
                "/github app prs",
                "/github app repos",
                "/github app diagnose",
                "/github app logs",
                "/github app run",
                "/github actions",
                "/github diagnose",
                "/github logs",
                "/github prs",
                "/github repos",
                "/github run",
                "/github-app",
                "/github-app actions",
                "/github-app diagnose",
                "/github-app logs",
                "/github-app prs",
                "/github-app run",
                "/agent",
                "/agents",
                "/hooks",
                "/integrations",
                "/limits",
                "/model",
                "/settings",
                "/theme",
                "/voice",
                "/worktree",
                "/update",
                "/version"
            ]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentCommandIsRequested_RunsTaskAgentSkill()
    {
        var pipeline = new CapturingSkillExecutionPipeline(SkillResult.Succeeded("agent response"));
        var service = new SlashCommandService(skillExecutionPipeline: pipeline);

        var result = await service.ExecuteAsync("/agent inspect the current workspace");

        result.Success.Should().BeTrue();
        result.Message.Should().Be("agent response");
        pipeline.LastPlan.Should().NotBeNull();
        pipeline.LastPlan!.SkillId.Should().Be("agents.run");
        pipeline.LastPlan.Arguments["task"].GetString().Should().Be("inspect the current workspace");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentCommandHasNoTask_ReturnsUsage()
    {
        var service = new SlashCommandService(
            skillExecutionPipeline: new CapturingSkillExecutionPipeline(SkillResult.Succeeded("unused")));

        var result = await service.ExecuteAsync("/agent");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Usage: /agent <task>");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentCommandRuntimeIsUnavailable_ReturnsClearError()
    {
        var service = new SlashCommandService();

        var result = await service.ExecuteAsync("/agent inspect the workspace");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Task agent runtime is unavailable.");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentsRequested_ReturnsRuntimeRuns()
    {
        var runStore = new InMemoryRuntimeAgentRunStore();
        var run = runStore.Start(
            new RuntimeAgentRequest("CodingAgent-001", "coding", "Inspect the workspace."),
            "gpt-test");
        runStore.Complete(run.RunId, "done");
        var service = new SlashCommandService(runtimeAgentRunStore: runStore);

        var result = await service.ExecuteAsync("/agents");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("task runs:");
        result.Message.Should().Contain("CodingAgent-001 [Succeeded]");
        result.Message.Should().Contain("Completed.");
    }

    [Fact]
    public async Task ExecuteAsync_WhenModelIsRequested_ReturnsConfiguredRuntimeModel()
    {
        var service = new SlashCommandService(
            aiServiceOptions: Options.Create(new AIServiceConfiguration
            {
                Provider = "Anthropic",
                Endpoint = "https://api.anthropic.com",
                ModelId = "claude-sonnet-4-6"
            }));

        var result = await service.ExecuteAsync("/model");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("provider: Anthropic");
        result.Message.Should().Contain("model: claude-sonnet-4-6");
        result.Message.Should().Contain("planner, chat, skills, and agents");
    }

    [Theory]
    [InlineData("/models")]
    [InlineData("/quota")]
    [InlineData("/balance")]
    [InlineData("/rate-limit")]
    public async Task ExecuteAsync_WhenRuntimeAliasesAreRequested_ReturnsRuntimeStatus(string command)
    {
        var service = new SlashCommandService();

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain(command == "/models" ? "Kam AI runtime model:" : "Kam provider-limit warnings:");
    }

    [Fact]
    public void GetSuggestions_WhenGithubSubcommandIsTyped_IncludesRepositoryCommand()
    {
        var service = new SlashCommandService();

        var suggestions = service.GetSuggestions("/github r");

        suggestions.Select(command => command.Name)
            .Should()
            .Contain("/github repos");
    }

    [Fact]
    public void GetSuggestions_WhenGithubPullRequestSubcommandIsTyped_IncludesPullRequestCommand()
    {
        var service = new SlashCommandService();

        var suggestions = service.GetSuggestions("/github p");

        suggestions.Select(command => command.Name)
            .Should()
            .Contain("/github prs");
    }

    [Fact]
    public void GetSuggestions_WhenGithubActionsSubcommandIsTyped_IncludesActionsCommand()
    {
        var service = new SlashCommandService();

        var suggestions = service.GetSuggestions("/github a");

        suggestions.Select(command => command.Name)
            .Should()
            .Contain("/github actions");
    }

    [Fact]
    public void GetSuggestions_WhenGithubRunSubcommandIsTyped_IncludesRunCommand()
    {
        var service = new SlashCommandService();

        var suggestions = service.GetSuggestions("/github ru");

        suggestions.Select(command => command.Name)
            .Should()
            .Contain("/github run");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandsFilterIsProvided_ReturnsMatchingCommandHelp()
    {
        var service = new SlashCommandService();

        var result = await service.ExecuteAsync("/commands dep");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("/dependabot");
        result.Message.Should().NotContain("/plugins");
    }

    [Fact]
    public async Task ExecuteAsync_WhenMcpIsRequested_RedactsConfiguredSecret()
    {
        var service = new SlashCommandService(
            mcpOptions: Options.Create(new McpOptions
            {
                TodoistServerLink = "https://example.test/mcp",
                TodoistApiKey = "super-secret-token"
            }));

        var result = await service.ExecuteAsync("/mcp");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Todoist API key: (configured)");
        result.Message.Should().NotContain("super-secret-token");
    }

    [Fact]
    public async Task ExecuteAsync_WhenShellBackedWorkflowIsRequested_ReturnsSafeCodingAgentGuidance()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "kam-workspace");
        var service = new SlashCommandService(
            codingAgentOptions: Options.Create(new CodingAgentOptions
            {
                WorkspaceRoot = workspace,
                ApprovalMode = "workspace-write"
            }));

        var result = await service.ExecuteAsync("/dependabot");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("available in coding-agent mode");
        result.Message.Should().Contain(Path.GetFullPath(workspace));
        result.Message.Should().Contain("kam coding-agent /dependabot");
        result.Message.Should().Contain("do not run shell, git, or gh workflows directly");
    }

    [Fact]
    public async Task ExecuteAsync_WhenGithubAppIsRequested_ReturnsSafeSetupGuidance()
    {
        var service = new SlashCommandService();

        var result = await service.ExecuteAsync("/github-app");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("GitHub App");
        result.Message.Should().Contain("Metadata: read");
        result.Message.Should().Contain("Contents: read");
        result.Message.Should().Contain("dotnet user-secrets set \"GitHubApp:AppId\"");
        result.Message.Should().Contain("kam coding-agent /github app");
        result.Message.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public async Task ExecuteAsync_WhenGithubAppClientIsConnected_ReturnsConnectionStatus()
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    2),
                GitHubRepositoryListResult.Failed("not used")));

        var result = await service.ExecuteAsync("/github-app");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam GitHub App:");
        result.Message.Should().Contain("status: connected");
        result.Message.Should().Contain("repositories: 2 accessible");
        result.Message.Should().Contain("Metadata: read");
        result.Message.Should().NotContain("installation-token");
        result.Message.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public async Task ExecuteAsync_WhenGithubAppReposAreRequested_ListsAccessibleRepositories()
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
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
                    ])));

        var result = await service.ExecuteAsync("/github-app repos");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam GitHub App repositories:");
        result.Message.Should().Contain("Esquetta/Kam");
        result.Message.Should().Contain("private");
        result.Message.Should().Contain("default: master");
        result.Message.Should().Contain("Esquetta/PublicTool");
    }

    [Fact]
    public async Task ExecuteAsync_WhenGithubReposAliasIsRequested_ListsAccessibleRepositories()
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    1),
                GitHubRepositoryListResult.Succeeded(
                    "1 repository accessible.",
                    [
                        new GitHubRepositorySummary(
                            "Esquetta/Kam",
                            true,
                            "master",
                            "https://github.com/Esquetta/Kam",
                            "https://github.com/Esquetta/Kam.git")
                    ])));

        var result = await service.ExecuteAsync("/github repos");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam GitHub App repositories:");
        result.Message.Should().Contain("Esquetta/Kam");
    }

    [Theory]
    [InlineData("/github prs")]
    [InlineData("/github-app prs")]
    [InlineData("/github app prs")]
    public async Task ExecuteAsync_WhenGithubPrsAliasIsRequested_ListsOpenPullRequests(string command)
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    1),
                GitHubRepositoryListResult.Failed("not used"),
                GitHubPullRequestListResult.Succeeded(
                    "2 open pull requests across 1 repository.",
                    [
                        new GitHubPullRequestSummary(
                            "Esquetta/Kam",
                            42,
                            "Fix CI",
                            "open",
                            "alice",
                            "https://github.com/Esquetta/Kam/pull/42",
                            "feature/fix-ci",
                            "master",
                            false,
                            new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero)),
                        new GitHubPullRequestSummary(
                            "Esquetta/Kam",
                            43,
                            "Draft release notes",
                            "open",
                            "bob",
                            "https://github.com/Esquetta/Kam/pull/43",
                            "docs/release",
                            "master",
                            true,
                            new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero))
                    ])));

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam GitHub App pull requests:");
        result.Message.Should().Contain("Esquetta/Kam#42");
        result.Message.Should().Contain("Fix CI");
        result.Message.Should().Contain("alice");
        result.Message.Should().Contain("feature/fix-ci -> master");
        result.Message.Should().Contain("Draft release notes");
        result.Message.Should().Contain("draft");
        result.Message.Should().Contain("https://github.com/Esquetta/Kam/pull/42");
    }

    [Theory]
    [InlineData("/github actions")]
    [InlineData("/github-app actions")]
    [InlineData("/github app actions")]
    public async Task ExecuteAsync_WhenGithubActionsAliasIsRequested_ListsWorkflowRuns(string command)
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    1),
                GitHubRepositoryListResult.Failed("not used"),
                GitHubPullRequestListResult.Failed("not used"),
                GitHubWorkflowRunListResult.Succeeded(
                    "2 workflow runs across 1 repository.",
                    [
                        new GitHubWorkflowRunSummary(
                            "Esquetta/Kam",
                            1001,
                            ".NET CI",
                            "Add GitHub App PR slash command",
                            "completed",
                            "success",
                            "push",
                            "master",
                            "https://github.com/Esquetta/Kam/actions/runs/1001",
                            new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 10, 5, 0, TimeSpan.Zero)),
                        new GitHubWorkflowRunSummary(
                            "Esquetta/Kam",
                            1002,
                            "Security Scan",
                            "Check advisories",
                            "in_progress",
                            string.Empty,
                            "workflow_dispatch",
                            "master",
                            "https://github.com/Esquetta/Kam/actions/runs/1002",
                            new DateTimeOffset(2026, 5, 11, 10, 10, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 10, 12, 0, TimeSpan.Zero))
                    ])));

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam GitHub App workflow runs:");
        result.Message.Should().Contain("Esquetta/Kam#1001");
        result.Message.Should().Contain(".NET CI");
        result.Message.Should().Contain("completed/success");
        result.Message.Should().Contain("push");
        result.Message.Should().Contain("master");
        result.Message.Should().Contain("Add GitHub App PR slash command");
        result.Message.Should().Contain("Security Scan");
        result.Message.Should().Contain("in_progress");
        result.Message.Should().Contain("https://github.com/Esquetta/Kam/actions/runs/1001");
    }

    [Theory]
    [InlineData("/github run Esquetta/Kam 1001")]
    [InlineData("/github-app run Esquetta/Kam 1001")]
    [InlineData("/github app run Esquetta/Kam 1001")]
    public async Task ExecuteAsync_WhenGithubRunAliasIsRequested_ListsWorkflowRunJobs(string command)
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    1),
                GitHubRepositoryListResult.Failed("not used"),
                GitHubPullRequestListResult.Failed("not used"),
                GitHubWorkflowRunListResult.Failed("not used"),
                GitHubWorkflowJobListResult.Succeeded(
                    "2 jobs for workflow run 1001 in Esquetta/Kam.",
                    "Esquetta/Kam",
                    1001,
                    [
                        new GitHubWorkflowJobSummary(
                            "Esquetta/Kam",
                            1001,
                            2001,
                            "build",
                            "completed",
                            "success",
                            "https://github.com/Esquetta/Kam/actions/runs/1001/job/2001",
                            new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 10, 5, 0, TimeSpan.Zero)),
                        new GitHubWorkflowJobSummary(
                            "Esquetta/Kam",
                            1001,
                            2002,
                            "security-scan",
                            "completed",
                            "failure",
                            "https://github.com/Esquetta/Kam/actions/runs/1001/job/2002",
                            new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 10, 4, 0, TimeSpan.Zero))
                    ])));

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam GitHub App workflow run jobs:");
        result.Message.Should().Contain("run: Esquetta/Kam#1001");
        result.Message.Should().Contain("build");
        result.Message.Should().Contain("completed/success");
        result.Message.Should().Contain("security-scan");
        result.Message.Should().Contain("completed/failure");
        result.Message.Should().Contain("https://github.com/Esquetta/Kam/actions/runs/1001/job/2001");
        result.Message.Should().NotContain("installation-token");
        result.Message.Should().NotContain("PRIVATE KEY");
    }

    [Theory]
    [InlineData("/github diagnose Esquetta/Kam 1001")]
    [InlineData("/github-app diagnose Esquetta/Kam 1001")]
    [InlineData("/github app diagnose Esquetta/Kam 1001")]
    public async Task ExecuteAsync_WhenGithubDiagnoseRunIsRequested_ReportsFailedJobsAndSteps(string command)
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    1),
                GitHubRepositoryListResult.Failed("not used"),
                GitHubPullRequestListResult.Failed("not used"),
                GitHubWorkflowRunListResult.Failed("not used"),
                GitHubWorkflowJobListResult.Succeeded(
                    "2 jobs for workflow run 1001 in Esquetta/Kam.",
                    "Esquetta/Kam",
                    1001,
                    [
                        new GitHubWorkflowJobSummary(
                            "Esquetta/Kam",
                            1001,
                            2001,
                            "build",
                            "completed",
                            "success",
                            "https://github.com/Esquetta/Kam/actions/runs/1001/job/2001",
                            new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 10, 5, 0, TimeSpan.Zero)),
                        new GitHubWorkflowJobSummary(
                            "Esquetta/Kam",
                            1001,
                            2002,
                            "security-scan",
                            "completed",
                            "failure",
                            "https://github.com/Esquetta/Kam/actions/runs/1001/job/2002",
                            new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 10, 4, 0, TimeSpan.Zero))
                        {
                            Steps =
                            [
                                new GitHubWorkflowJobStepSummary(
                                    1,
                                    "Restore dependencies",
                                    "completed",
                                    "success",
                                    new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
                                    new DateTimeOffset(2026, 5, 11, 10, 1, 0, TimeSpan.Zero)),
                                new GitHubWorkflowJobStepSummary(
                                    2,
                                    "Audit vulnerable NuGet packages",
                                    "completed",
                                    "failure",
                                    new DateTimeOffset(2026, 5, 11, 10, 1, 0, TimeSpan.Zero),
                                    new DateTimeOffset(2026, 5, 11, 10, 2, 0, TimeSpan.Zero))
                            ]
                        }
                    ])));

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam GitHub CI doctor:");
        result.Message.Should().Contain("run: Esquetta/Kam#1001");
        result.Message.Should().Contain("diagnosis: 1 failing job, 1 failing step.");
        result.Message.Should().Contain("security-scan (completed/failure)");
        result.Message.Should().Contain("Audit vulnerable NuGet packages (completed/failure)");
        result.Message.Should().Contain("next: inspect job log");
        result.Message.Should().Contain("https://github.com/Esquetta/Kam/actions/runs/1001/job/2002");
        result.Message.Should().NotContain("installation-token");
        result.Message.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public async Task ExecuteAsync_WhenGithubDiagnoseRunIdIsOmitted_SelectsLatestUnhealthyRun()
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    1),
                GitHubRepositoryListResult.Failed("not used"),
                GitHubPullRequestListResult.Failed("not used"),
                GitHubWorkflowRunListResult.Succeeded(
                    "2 workflow runs across 1 repository.",
                    [
                        new GitHubWorkflowRunSummary(
                            "Esquetta/Kam",
                            1001,
                            ".NET CI",
                            "Previous success",
                            "completed",
                            "success",
                            "push",
                            "master",
                            "https://github.com/Esquetta/Kam/actions/runs/1001",
                            new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 9, 5, 0, TimeSpan.Zero)),
                        new GitHubWorkflowRunSummary(
                            "Esquetta/Kam",
                            1002,
                            ".NET CI",
                            "Broken push",
                            "completed",
                            "failure",
                            "push",
                            "master",
                            "https://github.com/Esquetta/Kam/actions/runs/1002",
                            new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 10, 5, 0, TimeSpan.Zero))
                    ]),
                GitHubWorkflowJobListResult.Succeeded(
                    "1 job for workflow run 1002 in Esquetta/Kam.",
                    "Esquetta/Kam",
                    1002,
                    [
                        new GitHubWorkflowJobSummary(
                            "Esquetta/Kam",
                            1002,
                            2002,
                            "build",
                            "completed",
                            "failure",
                            "https://github.com/Esquetta/Kam/actions/runs/1002/job/2002",
                            new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 5, 11, 10, 4, 0, TimeSpan.Zero))
                    ])));

        var result = await service.ExecuteAsync("/github diagnose Esquetta/Kam");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("run: Esquetta/Kam#1002");
        result.Message.Should().Contain("selected: latest failing or in-progress workflow run");
        result.Message.Should().Contain("build (completed/failure)");
    }

    [Theory]
    [InlineData("/github diagnose")]
    [InlineData("/github diagnose Esquetta/Kam abc")]
    [InlineData("/github-app diagnose Esquetta/Kam 0")]
    [InlineData("/github app diagnose Esquetta/Kam -1")]
    public async Task ExecuteAsync_WhenGithubDiagnoseArgumentsAreInvalid_ReturnsUsage(string command)
    {
        var service = new SlashCommandService();

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Usage: /github diagnose <owner/repo> [runId]");
    }

    [Theory]
    [InlineData("/github logs Esquetta/Kam 2001")]
    [InlineData("/github-app logs Esquetta/Kam 2001")]
    [InlineData("/github app logs Esquetta/Kam 2001")]
    public async Task ExecuteAsync_WhenGithubLogsAliasIsRequested_ReturnsTemporaryJobLogUrl(string command)
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    1),
                GitHubRepositoryListResult.Failed("not used"),
                workflowJobLog: GitHubWorkflowJobLogResult.Succeeded(
                    "GitHub returned a temporary workflow job log download URL.",
                    "Esquetta/Kam",
                    2001,
                    "https://pipelines.actions.githubusercontent.com/logs/2001",
                    string.Empty)));

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam GitHub App workflow job logs:");
        result.Message.Should().Contain("job: Esquetta/Kam#2001");
        result.Message.Should().Contain("download: https://pipelines.actions.githubusercontent.com/logs/2001");
        result.Message.Should().Contain("expires: GitHub log download URLs expire after 1 minute");
        result.Message.Should().NotContain("installation-token");
        result.Message.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public async Task ExecuteAsync_WhenGithubLogsPreviewIsReturned_RedactsAndTruncatesPreview()
    {
        var service = new SlashCommandService(
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding",
                    "kam-coding",
                    1),
                GitHubRepositoryListResult.Failed("not used"),
                workflowJobLog: GitHubWorkflowJobLogResult.Succeeded(
                    "Downloaded workflow job log preview.",
                    "Esquetta/Kam",
                    2001,
                    string.Empty,
                    "Run dotnet test\ntoken=secret-value-token\nBuild failed")));

        var result = await service.ExecuteAsync("/github logs Esquetta/Kam 2001");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("preview:");
        result.Message.Should().Contain("Run dotnet test");
        result.Message.Should().Contain("Build failed");
        result.Message.Should().NotContain("secret-value-token");
    }

    [Theory]
    [InlineData("/github logs")]
    [InlineData("/github logs Esquetta/Kam abc")]
    [InlineData("/github-app logs Esquetta/Kam 0")]
    [InlineData("/github app logs Esquetta/Kam -1")]
    public async Task ExecuteAsync_WhenGithubLogsArgumentsAreInvalid_ReturnsUsage(string command)
    {
        var service = new SlashCommandService();

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Usage: /github logs <owner/repo> <jobId>");
    }

    [Theory]
    [InlineData("/github run Esquetta/Kam")]
    [InlineData("/github run Esquetta/Kam abc")]
    [InlineData("/github-app run Esquetta/Kam 0")]
    [InlineData("/github app run Esquetta/Kam -1")]
    public async Task ExecuteAsync_WhenGithubRunIdIsMissingOrInvalid_ReturnsUsage(string command)
    {
        var service = new SlashCommandService();

        var result = await service.ExecuteAsync(command);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Usage: /github run <owner/repo> <runId>");
    }

    [Fact]
    public async Task ExecuteAsync_WhenVersionIsRequested_ReturnsCurrentVersion()
    {
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService());

        var result = await service.ExecuteAsync("/version");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("current: 1.0.0");
    }

    [Fact]
    public async Task ExecuteAsync_WhenVersionProviderIsAvailable_UsesSharedApplicationVersion()
    {
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService(currentVersion: "1.0.0"),
            applicationVersionProvider: new FakeApplicationVersionProvider("2.0.0"));

        var result = await service.ExecuteAsync("/version");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("current: 2.0.0");
        result.Message.Should().NotContain("current: 1.0.0");
    }

    [Fact]
    public async Task ExecuteAsync_WhenUpdateIsRequested_ReturnsReleaseStatus()
    {
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService());

        var result = await service.ExecuteAsync("/update");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("update available");
        result.Message.Should().Contain("Kam-1.2.0-x64.msi");
    }

    [Fact]
    public async Task ExecuteAsync_WhenUpdateHasNoReleaseAsset_DoesNotOfferDownloadNextStep()
    {
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService(
                checkResult: ApplicationUpdateCheckResult.UpdateAvailable(
                    "1.0.0",
                    "1.2.0",
                    "Kam 1.2.0",
                    "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                    DateTimeOffset.Parse("2026-05-09T12:00:00Z"),
                    asset: null)));

        var result = await service.ExecuteAsync("/update");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("status: update available");
        result.Message.Should().Contain("download: no release package asset found");
        result.Message.Should().NotContain("next: /download");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDownloadIsRequested_ReturnsDownloadedFilePath()
    {
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService());

        var result = await service.ExecuteAsync("/download");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain(@"C:\Updates\Kam-1.2.0-x64.msi");
        result.Message.Should().Contain("verification: SHA256 verified");
        result.Message.Should().Contain("next: /restart <file>");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDownloadIsUnverified_DoesNotOfferRestartAsNextStep()
    {
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService(
                ApplicationUpdateDownloadResult.Succeeded(
                    @"C:\Updates\Kam-1.2.0-x64.msi",
                    "1.2.0",
                    1024,
                    isVerified: false,
                    verificationStatus: "Checksum missing")));

        var result = await service.ExecuteAsync("/download");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("verification: Checksum missing");
        result.Message.Should().Contain("next: verify release package before restart");
        result.Message.Should().Contain("restart: blocked until package verification succeeds");
        result.Message.Should().NotContain("next: /restart <file>");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRestartIsRequested_ReturnsRestartPlan()
    {
        var package = CreatePackage("Kam Updates", "Kam-1.2.0-x64.msi", "verified-package");
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService(
                downloadResult: ApplicationUpdateDownloadResult.Succeeded(
                    package.Path,
                    "1.2.0",
                    package.SizeBytes,
                    isVerified: true,
                    verificationStatus: "SHA256 verified",
                    expectedSha256: package.Sha256,
                    actualSha256: package.Sha256)),
            applicationRestartPlanner: new FakeApplicationRestartPlanner());

        try
        {
            await service.ExecuteAsync("/download");
            var result = await service.ExecuteAsync($"/restart \"{package.Path}\"");

            result.Success.Should().BeTrue();
            result.Message.Should().Contain("Kam restart plan");
            result.Message.Should().Contain(package.Path);
            result.Message.Should().Contain("Start installer");
        }
        finally
        {
            Directory.Delete(package.DirectoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerifiedRestartPackageWasModified_BlocksInstallerHandoff()
    {
        var package = CreatePackage("Kam Updates", "Kam-1.2.0-x64.msi", "verified-package");
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService(
                downloadResult: ApplicationUpdateDownloadResult.Succeeded(
                    package.Path,
                    "1.2.0",
                    package.SizeBytes,
                    isVerified: true,
                    verificationStatus: "SHA256 verified",
                    expectedSha256: package.Sha256,
                    actualSha256: package.Sha256)),
            applicationRestartPlanner: new FakeApplicationRestartPlanner());

        try
        {
            await service.ExecuteAsync("/download");
            File.AppendAllText(package.Path, "-tampered");

            var result = await service.ExecuteAsync($"/restart \"{package.Path}\"");

            result.Success.Should().BeTrue();
            result.Message.Should().Contain("status: blocked");
            result.Message.Should().Contain("SHA256 no longer matches");
            result.Message.Should().NotContain("Start installer");
        }
        finally
        {
            Directory.Delete(package.DirectoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenRestartPackageWasNotVerified_BlocksInstallerHandoff()
    {
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService(
                ApplicationUpdateDownloadResult.Succeeded(
                    @"C:\Updates\Kam-1.2.0-x64.msi",
                    "1.2.0",
                    1024,
                    isVerified: false,
                    verificationStatus: "Checksum missing")),
            applicationRestartPlanner: new FakeApplicationRestartPlanner());

        await service.ExecuteAsync("/download");
        var result = await service.ExecuteAsync(@"/restart C:\Updates\Kam-1.2.0-x64.msi");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("status: blocked");
        result.Message.Should().Contain("Restart handoff requires a verified package downloaded in the current session.");
        result.Message.Should().NotContain("Start installer");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRestartPackageWasNotDownloadedInSlashSession_BlocksInstallerHandoff()
    {
        var service = new SlashCommandService(
            applicationRestartPlanner: new FakeApplicationRestartPlanner());

        var result = await service.ExecuteAsync(@"/restart C:\Updates\Kam-1.2.0-x64.msi");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("status: blocked");
        result.Message.Should().Contain("Restart handoff requires a verified package downloaded in the current session.");
        result.Message.Should().NotContain("Start installer");
    }

    private static (string DirectoryPath, string Path, long SizeBytes, string Sha256) CreatePackage(
        string directoryName,
        string fileName,
        string contents)
    {
        var directoryPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "kam-slash-command-tests",
            directoryName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        var packagePath = System.IO.Path.Combine(directoryPath, fileName);
        File.WriteAllText(packagePath, contents);
        return (directoryPath, packagePath, new FileInfo(packagePath).Length, ComputeSha256(packagePath));
    }

    private static string ComputeSha256(string filePath)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();
    }

    private sealed class FakeApplicationUpdateService : IApplicationUpdateService
    {
        private readonly ApplicationUpdateCheckResult? _checkResult;
        private readonly ApplicationUpdateDownloadResult _downloadResult;

        public FakeApplicationUpdateService()
            : this(ApplicationUpdateDownloadResult.Succeeded(
                @"C:\Updates\Kam-1.2.0-x64.msi",
                "1.2.0",
                1024,
                isVerified: true,
                verificationStatus: "SHA256 verified",
                expectedSha256: new string('a', 64),
                actualSha256: new string('a', 64)))
        {
        }

        public FakeApplicationUpdateService(
            ApplicationUpdateDownloadResult? downloadResult = null,
            ApplicationUpdateCheckResult? checkResult = null,
            string currentVersion = "1.0.0")
        {
            _downloadResult = downloadResult ?? ApplicationUpdateDownloadResult.Succeeded(
                @"C:\Updates\Kam-1.2.0-x64.msi",
                "1.2.0",
                1024,
                isVerified: true,
                verificationStatus: "SHA256 verified",
                expectedSha256: new string('a', 64),
                actualSha256: new string('a', 64));
            _checkResult = checkResult;
            CurrentVersion = currentVersion;
        }

        public string CurrentVersion { get; }

        public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_checkResult ?? ApplicationUpdateCheckResult.UpdateAvailable(
                "1.0.0",
                "1.2.0",
                "Kam 1.2.0",
                "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                DateTimeOffset.Parse("2026-05-09T12:00:00Z"),
                new ApplicationUpdateAsset(
                    "Kam-1.2.0-x64.msi",
                    "https://downloads.example/Kam-1.2.0-x64.msi",
                    1024,
                    "application/octet-stream")));
        }

        public Task<ApplicationUpdateDownloadResult> DownloadLatestAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_downloadResult);
        }
    }

    private sealed class FakeApplicationVersionProvider : IApplicationVersionProvider
    {
        public FakeApplicationVersionProvider(string currentVersion)
        {
            CurrentVersion = currentVersion;
        }

        public string CurrentVersion { get; }
    }

    private sealed class FakeApplicationRestartPlanner : IApplicationRestartPlanner
    {
        public ApplicationRestartPlan CreateRestartPlan(string? updatePackagePath = null)
        {
            return new ApplicationRestartPlan(
                true,
                "ready",
                @"C:\Kam\Kam.exe",
                updatePackagePath,
                ["Start installer", "Close Kam"]);
        }
    }

    private sealed class StaticGitHubAppClient : IGitHubAppClient
    {
        private readonly GitHubAppConnectionStatus _status;
        private readonly GitHubRepositoryListResult _repositories;
        private readonly GitHubPullRequestListResult _pullRequests;
        private readonly GitHubWorkflowRunListResult _workflowRuns;
        private readonly GitHubWorkflowJobListResult _workflowJobs;
        private readonly GitHubWorkflowJobLogResult _workflowJobLog;

        public StaticGitHubAppClient(
            GitHubAppConnectionStatus status,
            GitHubRepositoryListResult repositories,
            GitHubPullRequestListResult? pullRequests = null,
            GitHubWorkflowRunListResult? workflowRuns = null,
            GitHubWorkflowJobListResult? workflowJobs = null,
            GitHubWorkflowJobLogResult? workflowJobLog = null)
        {
            _status = status;
            _repositories = repositories;
            _pullRequests = pullRequests ?? GitHubPullRequestListResult.Failed("not used");
            _workflowRuns = workflowRuns ?? GitHubWorkflowRunListResult.Failed("not used");
            _workflowJobs = workflowJobs ?? GitHubWorkflowJobListResult.Failed("not used");
            _workflowJobLog = workflowJobLog ?? GitHubWorkflowJobLogResult.Failed("not used");
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

        public Task<GitHubPullRequestListResult> ListPullRequestsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_pullRequests);
        }

        public Task<GitHubWorkflowRunListResult> ListWorkflowRunsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_workflowRuns);
        }

        public Task<GitHubWorkflowJobListResult> ListWorkflowRunJobsAsync(
            string repositoryFullName,
            long runId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_workflowJobs);
        }

        public Task<GitHubWorkflowJobLogResult> GetWorkflowJobLogAsync(
            string repositoryFullName,
            long jobId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_workflowJobLog);
        }
    }

    private sealed class CapturingSkillExecutionPipeline : ISkillExecutionPipeline
    {
        private readonly SkillResult _result;

        public CapturingSkillExecutionPipeline(SkillResult result)
        {
            _result = result;
        }

        public SkillPlan? LastPlan { get; private set; }

        public Task<SkillResult> ExecuteAsync(
            SkillPlan plan,
            CancellationToken cancellationToken = default)
        {
            LastPlan = plan;
            return Task.FromResult(_result);
        }
    }
}
