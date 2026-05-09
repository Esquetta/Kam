using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Models.CodingAgent;
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
            .Contain(["/dependabot", "/diff", "/github", "/hooks", "/worktree"]);
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
}
