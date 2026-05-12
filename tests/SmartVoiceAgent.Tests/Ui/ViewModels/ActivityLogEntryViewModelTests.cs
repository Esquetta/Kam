using FluentAssertions;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class ActivityLogEntryViewModelTests
{
    [Theory]
    [InlineData("> /status", "Command", "/status")]
    [InlineData("AGENT_RUNTIME_READY", "Ready", "AGENT_RUNTIME_READY")]
    [InlineData("NAVIGATED_TO: SETTINGS", "System", "NAVIGATED_TO: SETTINGS")]
    [InlineData("COPY_FAILED: clipboard unavailable", "Error", "COPY_FAILED: clipboard unavailable")]
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
    public void Create_CleansCommonStatusPrefixes()
    {
        var entry = ActivityLogEntryViewModel.Create("12:34:56", "✅ Runtime ready");

        entry.CategoryText.Should().Be("Ready");
        entry.MessageText.Should().Be("Runtime ready");
    }
}
