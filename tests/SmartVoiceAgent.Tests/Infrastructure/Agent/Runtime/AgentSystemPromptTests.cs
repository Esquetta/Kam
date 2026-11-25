using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Runtime;

public sealed class AgentSystemPromptTests
{
    [Fact]
    public void Build_TiesTheReplyLanguageToTheLatestMessage()
    {
        var prompt = AgentSystemPrompt.Build(new DateTimeOffset(2026, 11, 25, 11, 34, 0, TimeSpan.FromHours(3)), toolCount: 12);

        prompt.Should().Contain("Reply in the language of the user's latest message");
    }

    [Fact]
    public void Build_GivesTheTimeAsAnOffsetWithoutARegionName()
    {
        var prompt = AgentSystemPrompt.Build(new DateTimeOffset(2026, 11, 25, 11, 34, 0, TimeSpan.FromHours(3)), toolCount: 12);

        prompt.Should().Contain("The current local time is 2026-11-25 11:34 (UTC+03:00).");
        prompt.Should().NotContain(TimeZoneInfo.Local.DisplayName,
            "a city name such as Istanbul in the prompt was the likeliest reason English requests got Turkish replies");
    }
}
