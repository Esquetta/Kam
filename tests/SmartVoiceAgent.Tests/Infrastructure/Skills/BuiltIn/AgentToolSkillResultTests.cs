using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public sealed class AgentToolSkillResultTests
{
    [Fact]
    public void FromMessage_WithFailureTextInsideClipboardContent_ReturnsSuccess()
    {
        var result = AgentToolSkillResult.FromMessage(
            "Clipboard content:\n```\nFatal error! Unhandled exception\n```");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Fatal error");
    }
}
