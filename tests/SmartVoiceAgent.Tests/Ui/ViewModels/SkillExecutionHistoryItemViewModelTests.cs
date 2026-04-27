using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
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
            Truncated = true
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
    }
}
