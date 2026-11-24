using FluentAssertions;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public class AgentTurnTraceViewModelTests
{
    [Fact]
    public void Complete_RecordsTheOutcomeAndTotals()
    {
        var trace = new AgentTurnTraceViewModel("Rename the drafts\nplease");
        var step = AgentChatMessageViewModel.ToolStep("1", "files_move", "Move File", "{}", ToolRisk.Write);
        trace.AddStep(step);
        step.CompleteTool(true, "File moved");

        trace.Complete(toolCallCount: 1, inputTokens: 9120, outputTokens: 310);

        trace.Request.Should().Be("Rename the drafts please");
        trace.HasSteps.Should().BeTrue();
        trace.IsDone.Should().BeTrue();
        trace.OutcomeText.Should().Be("Done");
        trace.SummaryText.Should().StartWith("Tools: 1 · Time: ").And.EndWith("Tokens: 9,120 in, 310 out");
        step.ToolDurationText.Should().NotBeEmpty();
    }

    [Fact]
    public void End_MarksAStoppedTurnOnce()
    {
        var trace = new AgentTurnTraceViewModel("Count TODOs");

        trace.End(stopped: true);
        trace.End(stopped: false);

        trace.Outcome.Should().Be(AgentTurnOutcome.Stopped);
        trace.IsFailed.Should().BeTrue();
        trace.SummaryText.Should().StartWith("Tools: 0 · Time: ");
    }

    [Theory]
    [InlineData(0.25, "250 ms")]
    [InlineData(4.2, "4.2 s")]
    [InlineData(42, "42 s")]
    [InlineData(125, "2 min 5 s")]
    public void FormatDuration_PicksAReadableUnit(double seconds, string expected)
    {
        AgentTurnTraceViewModel.FormatDuration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }
}
