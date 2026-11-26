using FluentAssertions;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class AgentChatSessionStatusTests
{
    [Fact]
    public void HasPendingApproval_FollowsAToolCallWaitingForTheUser()
    {
        var session = AgentChatSessionViewModel.Create("Clean up Downloads", string.Empty, "now");
        var step = AgentChatMessageViewModel.ToolStep("c1", "files_move", "Move File", "{}", ToolRisk.Write);
        session.AddMessage(step);
        session.IsRunning = true;

        session.HasPendingApproval.Should().BeFalse();
        session.IsWorking.Should().BeTrue();

        step.RequestApproval("r1");

        session.HasPendingApproval.Should().BeTrue();
        session.IsWorking.Should().BeFalse("a turn waiting on the user is not shown as working");
        session.HasStatus.Should().BeTrue();
        session.StatusText.Should().Be(step.ToolStatusText, "the list says the same as the step's pill");

        step.ApprovalAnswered(approved: true);

        session.HasPendingApproval.Should().BeFalse();
        session.IsWorking.Should().BeTrue();
    }

    [Fact]
    public void HasPendingApproval_ClearsWhenTheWaitingStepIsRemovedOrHistoryReplaced()
    {
        var session = AgentChatSessionViewModel.Create("Task", string.Empty, "now");
        var step = AgentChatMessageViewModel.ToolStep("c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);
        session.AddMessage(step);
        step.RequestApproval("r1");
        session.HasPendingApproval.Should().BeTrue();

        session.RemoveMessage(step);
        session.HasPendingApproval.Should().BeFalse();

        var waiting = AgentChatMessageViewModel.ToolStep("c2", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);
        waiting.RequestApproval("r2");
        session.LoadHistory([waiting]);
        session.HasPendingApproval.Should().BeTrue();

        session.LoadHistory([]);
        session.HasPendingApproval.Should().BeFalse();
        session.HasStatus.Should().BeFalse();
    }

    [Fact]
    public void ToolDurationText_LeavesOutTheWaitForApproval()
    {
        var step = AgentChatMessageViewModel.ToolStep("c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);

        step.RequestApproval("r1");
        Thread.Sleep(1200);
        step.ApprovalAnswered(approved: true);
        step.CompleteTool(success: true, "git version 2.56.0");

        step.ToolDurationText.Should().EndWith("ms", "the step ran for milliseconds; the 1.2 s before Approve was the user's time");
    }

    [Fact]
    public void ToolDurationText_LeavesOutTheWaitWhenTheTurnStopsDuringApproval()
    {
        var step = AgentChatMessageViewModel.ToolStep("c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);

        step.RequestApproval("r1");
        Thread.Sleep(1200);
        step.Abandon();

        step.ToolDurationText.Should().EndWith("ms");
    }

    [Fact]
    public void ShowsToolDetails_OnlyWhenOpenedOrWaiting()
    {
        var step = AgentChatMessageViewModel.ToolStep("c1", "files_read", "Read File", "{\"path\":\"a.txt\"}", ToolRisk.Read);

        step.ShowsToolDetails.Should().BeFalse("a running step is one line");

        step.RequestApproval("r1");
        step.ShowsToolDetails.Should().BeTrue("the approval card needs the arguments");

        step.ApprovalAnswered(approved: true);
        step.CompleteTool(success: true, "read 12 lines");
        step.ShowsToolDetails.Should().BeFalse("a finished step folds back to one line");

        step.IsExpanded = true;
        step.ShowsToolDetails.Should().BeTrue();
        step.ShowsToolResult.Should().BeTrue();
        step.ShowsToolError.Should().BeFalse();

        var failed = AgentChatMessageViewModel.ToolStep("c2", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);
        failed.CompleteTool(success: false, "exit code 1");
        failed.ShowsToolDetails.Should().BeFalse("a failed step stays one line");
        failed.ShowsToolError.Should().BeTrue("its error shows under the line");
        failed.ShowsToolResult.Should().BeFalse("the error is not repeated in the details");
    }

    [Fact]
    public void HasPendingApproval_FollowsAStepInsideASubagentCard()
    {
        var session = AgentChatSessionViewModel.Create("Subagents", string.Empty, "now");
        var task = AgentChatMessageViewModel.ToolStep("t1", "task", "Subagent", "{\"subagent_type\":\"general\"}", ToolRisk.Agent);
        session.AddToolStep(task);
        var child = AgentChatMessageViewModel.ToolStep("c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);
        child.MarkSubagentStep("general");
        task.Children.Add(child);

        child.RequestApproval("r1");

        session.HasPendingApproval.Should().BeTrue();
        session.ToolSteps.Should().Equal(task, child);

        child.ApprovalAnswered(approved: false);

        session.HasPendingApproval.Should().BeFalse();
    }
}
