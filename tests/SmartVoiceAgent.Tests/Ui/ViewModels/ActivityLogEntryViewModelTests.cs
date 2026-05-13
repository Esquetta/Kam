using FluentAssertions;
using SmartVoiceAgent.Core.Models.Agents;
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

    [Fact]
    public void Create_DisplaysRuntimeAgentUpdatesWithFriendlyAgentSource()
    {
        var entry = ActivityLogEntryViewModel.Create(
            "12:34:56",
            "[CodingAgent-001] Completed.");

        entry.CategoryText.Should().Be("Ready");
        entry.SourceText.Should().Be("Coding agent 1");
        entry.HasSourceText.Should().BeTrue();
        entry.MessageText.Should().Be("Completed.");
    }

    [Fact]
    public void RuntimeAgentActivityViewModel_Create_FormatsDisplayNameAndStatus()
    {
        var running = RuntimeAgentActivityViewModel.Create(
            "DesignAgent-007",
            "Created automatically for this request.",
            isComplete: false);
        var completed = RuntimeAgentActivityViewModel.Create(
            "DesignAgent-007",
            "Completed.",
            isComplete: true);

        running.DisplayName.Should().Be("Design agent 7");
        running.RunId.Should().BeNull();
        running.StatusText.Should().Be("Running");
        running.LastMessage.Should().Be("Created automatically for this request.");
        completed.StatusText.Should().Be("Done");
    }

    [Fact]
    public void RuntimeAgentActivityViewModel_Create_PreservesRunId()
    {
        var activity = RuntimeAgentActivityViewModel.Create(
            "CodingAgent-001",
            "Created automatically for this request.",
            isComplete: false,
            runId: "run_123");

        activity.RunId.Should().Be("run_123");
    }

    [Fact]
    public void RuntimeAgentRunDetailViewModel_Create_ProjectsFriendlyRunMetadata()
    {
        var run = new RuntimeAgentRun(
            "run_123",
            "CodingAgent-001",
            "frontend",
            "Improve the activity panel.",
            "gpt-5.5",
            RuntimeAgentRunStatus.Succeeded,
            new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 13, 10, 1, 5, TimeSpan.Zero),
            LastMessage: "Completed.",
            Response: "Done",
            ToolObservations:
            [
                new RuntimeAgentToolObservation("tool.1", "internal", true),
                new RuntimeAgentToolObservation("workspace.search_text", "matches", true)
            ]);

        var detail = RuntimeAgentRunDetailViewModel.Create(run);

        detail.DisplayName.Should().Be("Coding agent 1");
        detail.StatusText.Should().Be("Completed");
        detail.ModelIdText.Should().Be("gpt-5.5");
        detail.DurationText.Should().Be("1m 05s");
        detail.TaskText.Should().Be("Improve the activity panel.");
        detail.ResponseText.Should().Be("Done");
        detail.HasObservations.Should().BeTrue();
        detail.Observations[0].DisplayName.Should().Be("Context");
        detail.Observations[0].DisplayName.Should().NotContain("tool.1");
        detail.Observations[1].DisplayName.Should().Be("Search text");
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
