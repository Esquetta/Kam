using FluentAssertions;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class ActivityLogEntryViewModelTests
{
    [Theory]
    [InlineData("> /status", "Command", "/status")]
    [InlineData("AGENT_RUNTIME_READY", "Ready", "Agent runtime ready.")]
    [InlineData("NAVIGATED_TO: SETTINGS", "System", "Opened Settings.")]
    [InlineData("COPY_FAILED: clipboard unavailable", "Error", "Clipboard is not available.")]
    [InlineData("Voice service not available", "Warning", "Voice service not available")]
    public void Create_ClassifiesActivityMessages(string message, string expectedCategory, string expectedMessage)
    {
        var entry = ActivityLogEntryViewModel.Create("12:34:56", message);

        entry.TimeText.Should().Be("12:34:56");
        entry.CategoryText.Should().Be(expectedCategory);
        entry.MessageText.Should().Be(expectedMessage);
        entry.AccentBrush.Should().NotBeNull();
    }

    [Fact]
    public void Create_SplitsLeadingLogSourceFromMessage()
    {
        var entry = ActivityLogEntryViewModel.Create(
            "12:34:56",
            "[VoiceAgentHostedService] Ready for commands...");

        entry.CategoryText.Should().Be("Ready");
        entry.SourceText.Should().Be("Runtime");
        entry.HasSourceText.Should().BeTrue();
        entry.MessageText.Should().Be("Ready for commands...");
    }

    [Theory]
    [InlineData("[AgentRegistry] Agent registered: ResearchAgent", "Agents", "Research agent registered.")]
    [InlineData("[AgentFactory] Creating CommunicationAgent with optimized instructions...", "Agents", "Preparing communication agent.")]
    [InlineData("[CommunicationAgentTools] ISmsService not registered. SMS functionality will be unavailable.", "Integrations", "SMS integration is not configured.")]
    [InlineData("[MultiSTTService] HuggingFace API key not configured, skipping HuggingFace initialization", "Voice", "Speech provider is not configured.")]
    public void Create_MapsTechnicalLogSourcesToFriendlyLabels(
        string message,
        string expectedSource,
        string expectedMessage)
    {
        var entry = ActivityLogEntryViewModel.Create("12:34:56", message);

        entry.SourceText.Should().Be(expectedSource);
        entry.MessageText.Should().Be(expectedMessage);
        entry.SourceText.Should().NotContain("Service");
        entry.SourceText.Should().NotContain("AgentRegistry");
        entry.SourceText.Should().NotContain("AgentFactory");
        entry.MessageText.Should().NotContain("ISmsService");
        entry.MessageText.Should().NotContain("CommunicationAgentTools");
        entry.MessageText.Should().NotContain("MultiSTTService");
    }

    [Theory]
    [InlineData("AI_PROVIDER_RATE_LIMIT: Model: claude-sonnet-4-6", "Warning", "Claude", "Claude is rate limited. Wait for the reset or switch models.")]
    [InlineData("AI_PROVIDER_BALANCE: Model: gpt-5.5", "Warning", "Codex", "Codex quota or balance needs attention.")]
    [InlineData("COPIED_STDOUT", "System", "", "Copied output.")]
    [InlineData("THEME_CHANGED: DARK", "System", "", "Switched to dark theme.")]
    public void Create_RewritesRuntimeTokensToUserFacingCopy(
        string message,
        string expectedCategory,
        string expectedSource,
        string expectedMessage)
    {
        var entry = ActivityLogEntryViewModel.Create("12:34:56", message);

        entry.CategoryText.Should().Be(expectedCategory);
        entry.SourceText.Should().Be(expectedSource);
        entry.MessageText.Should().Be(expectedMessage);
        entry.MessageText.Should().NotContain("AI_PROVIDER_");
        entry.MessageText.Should().NotContain("COPIED_STDOUT");
        entry.MessageText.Should().NotContain("THEME_CHANGED");
    }

    [Fact]
    public void Create_CleansCommonStatusPrefixes()
    {
        var entry = ActivityLogEntryViewModel.Create("12:34:56", "\u2705 Runtime ready");

        entry.CategoryText.Should().Be("Ready");
        entry.MessageText.Should().Be("Runtime ready");
    }
}
