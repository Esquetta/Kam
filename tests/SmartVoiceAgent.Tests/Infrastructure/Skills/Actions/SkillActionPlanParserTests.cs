using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Skills.Actions;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Actions;

public sealed class SkillActionPlanParserTests
{
    [Fact]
    public void ParseStrict_ValidActionPlan_ReturnsPlan()
    {
        var result = SkillActionPlanParser.ParseStrict("""
        {"message":"Opening Notepad","actions":[{"type":"open_app","applicationName":"notepad"},{"type":"hotkey","keys":["ctrl","l"]}]}
        """);

        result.IsValid.Should().BeTrue();
        result.Plan!.Message.Should().Be("Opening Notepad");
        result.Plan.Actions.Should().HaveCount(2);
        result.Plan.Actions[0].Type.Should().Be("open_app");
        result.Plan.Actions[0].ApplicationName.Should().Be("notepad");
        result.Plan.Actions[1].Keys.Should().Equal("ctrl", "l");
    }

    [Fact]
    public void ParseStrict_TextAroundJson_ReturnsFailure()
    {
        var result = SkillActionPlanParser.ParseStrict("""
        I will do it.
        {"message":"Opening Notepad","actions":[]}
        """);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("single JSON object");
    }

    [Fact]
    public void ParseStrict_UnknownAction_ReturnsFailure()
    {
        var result = SkillActionPlanParser.ParseStrict("""
        {"message":"Run","actions":[{"type":"shell","command":"dir"}]}
        """);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported action");
    }
}
