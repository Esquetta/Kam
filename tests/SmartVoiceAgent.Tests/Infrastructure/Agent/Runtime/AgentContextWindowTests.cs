using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Runtime;

public sealed class AgentContextWindowTests
{
    [Fact]
    public void FindCompactionCut_KeepsTheNewestUserTurns()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "a"),
            new(ChatRole.Assistant, "1"),
            new(ChatRole.User, "b"),
            new(ChatRole.Assistant, "2"),
            new(ChatRole.User, "c")
        ];

        AgentContextWindow.FindCompactionCut(history, 2).Should().Be(2);
        AgentContextWindow.FindCompactionCut(history, 1).Should().Be(4);
        AgentContextWindow.FindCompactionCut(history, 0).Should().Be(5);
        AgentContextWindow.FindCompactionCut(history, 3).Should().BeNull("nothing comes before the first user turn");
    }

    [Fact]
    public void FindCompactionCut_StartsAfterTheNewestSummary()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "a"),
            AgentContextWindow.CreateSummaryMessage("old"),
            new(ChatRole.User, "b"),
            new(ChatRole.Assistant, "2")
        ];

        AgentContextWindow.FindActiveStart(history).Should().Be(2);
        AgentContextWindow.FindCompactionCut(history, 1).Should().BeNull();
        AgentContextWindow.FindCompactionCut(history, 0).Should().Be(4);
    }

    [Fact]
    public void BuildModelMessages_AfterSummary_SendsSummaryInSystemPromptAndOnlyNewerMessages()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "old question"),
            new(ChatRole.Assistant, "old answer"),
            AgentContextWindow.CreateSummaryMessage("The user asked an old question."),
            new(ChatRole.User, "new question")
        ];

        var messages = AgentContextWindow.BuildModelMessages("You are Kam.", history, shortenOldToolResults: false);

        messages.Select(m => m.Role).Should().Equal(ChatRole.System, ChatRole.User);
        messages[0].Text.Should().StartWith("You are Kam.").And.Contain("The user asked an old question.");
        messages[1].Text.Should().Be("new question");
    }

    [Fact]
    public void RenderTranscript_IncludesPreviousSummaryToolNamesAndClipsLongResults()
    {
        List<ChatMessage> history =
        [
            AgentContextWindow.CreateSummaryMessage("Earlier: user wants a report."),
            new(ChatRole.User, "read it"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "files_read", new Dictionary<string, object?> { ["path"] = "r.md" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", new string('z', 5000))]),
            new(ChatRole.Assistant, "It is long."),
            new(ChatRole.User, "kept")
        ];

        var transcript = AgentContextWindow.RenderTranscript(history, cut: 5, maxCharacters: 100000);

        transcript.Should().StartWith(AgentContextWindow.SummaryPrefix);
        transcript.Should().Contain("Earlier: user wants a report.");
        transcript.Should().Contain("User: read it");
        transcript.Should().Contain("Kam called files_read {\"path\":\"r.md\"}");
        transcript.Should().Contain("Result of files_read: zzz");
        transcript.Should().Contain("Kam: It is long.");
        transcript.Should().NotContain("kept");
        transcript.Length.Should().BeLessThan(3000);
    }

    [Fact]
    public void EstimateTokens_CountsTextCallsAndResults()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, new string('a', 400)),
            new(ChatRole.Tool, [new FunctionResultContent("c", new string('b', 800))])
        ];

        AgentContextWindow.EstimateTokens(history).Should().BeInRange(300, 320);
    }
}
