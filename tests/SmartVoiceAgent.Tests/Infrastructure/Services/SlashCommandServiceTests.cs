using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.CodingAgent;
using SmartVoiceAgent.Core.Models.Updates;
using SmartVoiceAgent.Infrastructure.Mcp;
using SmartVoiceAgent.Infrastructure.Services;

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
            .Contain(["/dependabot", "/diff", "/github", "/github-app", "/hooks", "/worktree", "/update", "/version"]);
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
    public async Task ExecuteAsync_WhenVersionIsRequested_ReturnsCurrentVersion()
    {
        var service = new SlashCommandService(
            applicationUpdateService: new FakeApplicationUpdateService());

        var result = await service.ExecuteAsync("/version");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("current: 1.0.0");
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
        result.Message.Should().NotContain("next: /restart <file>");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRestartIsRequested_ReturnsRestartPlan()
    {
        var service = new SlashCommandService(
            applicationRestartPlanner: new FakeApplicationRestartPlanner());

        var result = await service.ExecuteAsync(@"/restart C:\Program Files\Kam Updates\Kam-1.2.0-x64.msi");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Kam restart plan");
        result.Message.Should().Contain(@"C:\Program Files\Kam Updates\Kam-1.2.0-x64.msi");
        result.Message.Should().Contain("Start installer");
    }

    private sealed class FakeApplicationUpdateService : IApplicationUpdateService
    {
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

        public FakeApplicationUpdateService(ApplicationUpdateDownloadResult downloadResult)
        {
            _downloadResult = downloadResult;
        }

        public string CurrentVersion => "1.0.0";

        public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplicationUpdateCheckResult.UpdateAvailable(
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
}
