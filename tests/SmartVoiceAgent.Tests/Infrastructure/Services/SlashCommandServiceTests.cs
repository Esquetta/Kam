using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.CodingAgent;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Models.Updates;
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
                "/github actions",
                "/github prs",
                "/github repos",
                "/github-app",
                "/github-app actions",
                "/github-app prs",
                "/hooks",
                "/worktree",
                "/update",
                "/version"
            ]);
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

        public StaticGitHubAppClient(
            GitHubAppConnectionStatus status,
            GitHubRepositoryListResult repositories,
            GitHubPullRequestListResult? pullRequests = null,
            GitHubWorkflowRunListResult? workflowRuns = null)
        {
            _status = status;
            _repositories = repositories;
            _pullRequests = pullRequests ?? GitHubPullRequestListResult.Failed("not used");
            _workflowRuns = workflowRuns ?? GitHubWorkflowRunListResult.Failed("not used");
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
    }
}
