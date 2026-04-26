using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class ClipboardSkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ClipboardSetWithEmptyContent_NormalizesToolFailure()
    {
        var executor = new ClipboardSkillExecutor(new ClipboardTools());

        var result = await executor.ExecuteAsync(SkillPlan.FromObject("clipboard.set", new { content = "" }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cannot set empty clipboard content");
    }
}
