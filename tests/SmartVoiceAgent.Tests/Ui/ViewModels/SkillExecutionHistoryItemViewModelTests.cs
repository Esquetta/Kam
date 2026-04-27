using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Execution;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class SkillExecutionHistoryItemViewModelTests
{
    [Fact]
    public void Constructor_WhenEntryHasShellOutput_ExposesResultDetails()
    {
        var timestamp = new DateTimeOffset(
            2026,
            4,
            27,
            13,
            15,
            0,
            DateTimeOffset.Now.Offset);
        var entry = new SkillExecutionHistoryEntry
        {
            Timestamp = timestamp,
            SkillId = "shell.run",
            Success = false,
            Status = SkillExecutionStatus.Failed,
            ErrorCode = "shell_exit_code",
            ResultSummary = "Command failed.",
            ArgumentsSummary = "command=git status",
            DurationMilliseconds = 42,
            Command = "git status",
            WorkingDirectory = "C:\\repo",
            ExitCode = 1,
            StdOut = "tracked output",
            StdErr = "error output",
            Truncated = true,
            CanReplay = true,
            ReplayPlanJson = """{"skillId":"shell.run","arguments":{"command":"git status"}}"""
        };

        var viewModel = new SkillExecutionHistoryItemViewModel(entry);

        viewModel.SkillId.Should().Be("shell.run");
        viewModel.StatusText.Should().Be("Failed");
        viewModel.TimestampText.Should().Be("13:15:00");
        viewModel.DurationText.Should().Be("42 ms");
        viewModel.DetailText.Should().Contain("Command failed.");
        viewModel.DetailText.Should().Contain("shell_exit_code");
        viewModel.HasStdOut.Should().BeTrue();
        viewModel.HasStdErr.Should().BeTrue();
        viewModel.StdOut.Should().Be("tracked output");
        viewModel.StdErr.Should().Be("error output");
        viewModel.ExitCodeText.Should().Be("exit 1");
        viewModel.HasRuntimeFlags.Should().BeTrue();
        viewModel.RuntimeFlagsText.Should().Contain("truncated");
        viewModel.CanRerun.Should().BeTrue();
        viewModel.CopyResultText.Should().Contain("shell.run");
        viewModel.CopyResultText.Should().Contain("tracked output");
        viewModel.CopyResultText.Should().Contain("error output");
    }

    [Fact]
    public void ClearSkillExecutionHistoryCommand_WhenHistoryServiceIsAttached_ClearsService()
    {
        var service = new InMemorySkillExecutionHistoryService();
        service.Record(
            SkillPlan.FromObject("apps.list", new { }),
            SkillResult.Succeeded("Listed applications."));
        var viewModel = new MainWindowViewModel();
        viewModel.SetSkillExecutionHistoryService(service);

        viewModel.ClearSkillExecutionHistoryCommand.Execute(null);

        service.GetRecent().Should().BeEmpty();
    }

    [Fact]
    public void CopyCommands_InvokeCopyCallbackWithExpectedPayloads()
    {
        var copied = new List<(string Label, string Text)>();
        var viewModel = new SkillExecutionHistoryItemViewModel(
            new SkillExecutionHistoryEntry
            {
                SkillId = "shell.run",
                Status = SkillExecutionStatus.Succeeded,
                ResultSummary = "Done.",
                StdOut = "stdout text",
                StdErr = "stderr text"
            },
            (label, text) => copied.Add((label, text)));

        viewModel.CopyResultCommand.Execute(null);
        viewModel.CopyStdOutCommand.Execute(null);
        viewModel.CopyStdErrCommand.Execute(null);

        copied.Should().HaveCount(3);
        copied[0].Should().Be(("result", viewModel.CopyResultText));
        copied[1].Should().Be(("stdout", "stdout text"));
        copied[2].Should().Be(("stderr", "stderr text"));
    }

    [Fact]
    public void RerunCommand_WhenReplayIsAllowed_InvokeRerunCallback()
    {
        var rerunCount = 0;
        var viewModel = new SkillExecutionHistoryItemViewModel(
            new SkillExecutionHistoryEntry
            {
                SkillId = "apps.list",
                CanReplay = true,
                ReplayPlanJson = """{"skillId":"apps.list","arguments":{}}"""
            },
            (_, _) => { },
            _ => rerunCount++);

        viewModel.RerunCommand.Execute(null);

        rerunCount.Should().Be(1);
    }
}
