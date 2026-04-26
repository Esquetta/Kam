using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Skills.Planning;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Planning;

public class SkillPlanParserTests
{
    [Fact]
    public void Parse_JsonInsideMarkdownFence_ReturnsPlan()
    {
        const string response = """
        ```json
        {
          "skillId": "apps.open",
          "arguments": { "applicationName": "Spotify" },
          "confidence": 0.91,
          "requiresConfirmation": false,
          "reasoning": "Open Spotify"
        }
        ```
        """;

        var result = SkillPlanParser.Parse(response);

        result.IsValid.Should().BeTrue();
        result.Plan!.SkillId.Should().Be("apps.open");
        result.Plan.Arguments["applicationName"].GetString().Should().Be("Spotify");
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsActionableError()
    {
        var result = SkillPlanParser.Parse("I will open Spotify.");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("valid JSON");
    }

    [Fact]
    public void ParseStrictJsonObject_TextAroundJson_ReturnsActionableError()
    {
        const string response = """
        I will open Spotify.
        {"skillId":"apps.open","arguments":{"applicationName":"Spotify"}}
        """;

        var result = SkillPlanParser.ParseStrictJsonObject(response);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("single JSON object");
    }
}
