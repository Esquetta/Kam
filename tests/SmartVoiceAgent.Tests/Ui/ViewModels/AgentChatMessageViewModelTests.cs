using FluentAssertions;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class AgentChatMessageViewModelTests
{
    [Fact]
    public void AppendContent_WhileStreamingOnUiThread_RefreshesTextInBatches()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new HeldContext());
        try
        {
            var message = AgentChatMessageViewModel.Agent(string.Empty);
            message.IsStreaming = true;
            var refreshes = 0;
            message.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AgentChatMessageViewModel.Content))
                {
                    refreshes++;
                }
            };

            for (var token = 0; token < 100; token++)
            {
                message.AppendContent("x");
            }

            refreshes.Should().BeLessThan(10, "tokens arriving together share one refresh");

            message.IsStreaming = false;

            message.Content.Should().Be(new string('x', 100));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void AppendContent_WhenNotStreaming_ShowsTextAtOnce()
    {
        var message = AgentChatMessageViewModel.Agent("Hello");

        message.AppendContent(" world");

        message.Content.Should().Be("Hello world");
    }

    [Theory]
    [InlineData(ToolRisk.Read, false)]
    [InlineData(ToolRisk.Network, false)]
    [InlineData(ToolRisk.Agent, false)]
    [InlineData(ToolRisk.Write, true)]
    [InlineData(ToolRisk.Execute, true)]
    [InlineData(ToolRisk.External, true)]
    [InlineData(ToolRisk.Delegate, true)]
    public void ShowsRiskTag_OnlyForRisksThatMatter(ToolRisk risk, bool shown)
    {
        AgentChatMessageViewModel.ToolStep("c1", "tool", "Tool", "{}", risk).ShowsRiskTag.Should().Be(shown);
    }

    [Fact]
    public void SummarizeReadSteps_CountsFilesSearchesPagesAndTheRest()
    {
        AgentChatMessageViewModel.SummarizeReadSteps(
            [Step("files_read"), Step("file_read_range"), Step("workspace_search_text"), Step("web_search"), Step("web_fetch"), Step("load_skill")])
            .Should().Be("Explored 2 files · 2 searches · 1 web page · 1 other step");
        AgentChatMessageViewModel.SummarizeReadSteps([Step("load_skill"), Step("mcp__docs__get")])
            .Should().Be("Ran 2 read steps", "nothing here read a file, searched or opened a page");
    }

    [Fact]
    public void ToolGroup_FollowsItsSteps()
    {
        var group = AgentChatMessageViewModel.ToolGroup();
        var first = Step("files_read");
        var second = AgentChatMessageViewModel.ToolStep("c2", "files_search", "Search Files", "{\"pattern\":\"Foo\"}", ToolRisk.Read);
        first.CompleteTool(true, "ok");
        group.Children.Add(first);
        group.Children.Add(second);

        group.IsToolRunning.Should().BeTrue();
        group.GroupTitle.Should().Be("Search Files");
        group.GroupTarget.Should().Be("Foo");

        second.CompleteTool(false, "Error: bad pattern");

        group.IsToolSucceeded.Should().BeTrue();
        group.GroupTitle.Should().Be("Explored 1 file · 1 search");
        group.GroupTarget.Should().BeEmpty();
        group.GroupFailedText.Should().Be("1 failed");
        group.IsToolItem.Should().BeTrue();
        group.IsToolStep.Should().BeFalse("the group is not a call of its own");
    }

    [Fact]
    public void SubagentCard_OpensWhenOneOfItsStepsAsksForApproval()
    {
        var task = AgentChatMessageViewModel.ToolStep("t1", "task", "Subagent", "{\"subagent_type\":\"explorer\",\"description\":\"Find it\"}", ToolRisk.Agent);
        var child = AgentChatMessageViewModel.ToolStep("c1", "web_fetch", "Fetch Page", "{}", ToolRisk.Network);
        child.MarkSubagentStep("explorer");
        task.Children.Add(child);

        task.IsExpanded.Should().BeFalse();
        task.HasChildren.Should().BeTrue();

        child.RequestApproval("r1");

        task.IsExpanded.Should().BeTrue();
        task.HasChildAwaitingApproval.Should().BeTrue();
        task.ToolDisplayName.Should().Be("Explorer subagent");
        task.ToolTarget.Should().Be("Find it");
    }

    private static AgentChatMessageViewModel Step(string toolName) =>
        AgentChatMessageViewModel.ToolStep(toolName, toolName, toolName, "{}", ToolRisk.Read);

    /// <summary>A UI context whose posted callbacks never run, so the test controls every refresh.</summary>
    private sealed class HeldContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }
}
