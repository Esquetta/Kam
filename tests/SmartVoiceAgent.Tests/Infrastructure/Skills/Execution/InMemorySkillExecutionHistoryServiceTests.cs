using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Execution;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Execution;

public sealed class InMemorySkillExecutionHistoryServiceTests
{
    [Fact]
    public void Record_AddsNewestEntryFirstAndRaisesChanged()
    {
        var service = new InMemorySkillExecutionHistoryService(maxEntries: 10);
        var changedCount = 0;
        service.Changed += (_, _) => changedCount++;

        var first = service.Record(
            SkillPlan.FromObject("apps.open", new { applicationName = "Notepad", apiKey = "secret" }),
            SkillResult.Succeeded("Opened.") with { DurationMilliseconds = 12 },
            new DateTimeOffset(2026, 4, 27, 10, 0, 0, TimeSpan.Zero));
        service.Record(
            SkillPlan.FromObject("shell.run", new { command = "echo hello" }),
            SkillResult.Failed(
                "Denied.",
                SkillExecutionStatus.PermissionDenied,
                "permission_denied"),
            new DateTimeOffset(2026, 4, 27, 10, 1, 0, TimeSpan.Zero));

        var recent = service.GetRecent();

        changedCount.Should().Be(2);
        recent.Should().HaveCount(2);
        recent[0].SkillId.Should().Be("shell.run");
        recent[1].SkillId.Should().Be("apps.open");
        first.ArgumentsSummary.Should().Contain("applicationName=Notepad");
        first.ArgumentsSummary.Should().Contain("apiKey=<redacted>");
    }

    [Fact]
    public void Record_WhenCapacityIsExceeded_TrimsOldestEntries()
    {
        var service = new InMemorySkillExecutionHistoryService(maxEntries: 2);

        service.Record(
            SkillPlan.FromObject("first.skill", new { }),
            SkillResult.Succeeded("First."));
        service.Record(
            SkillPlan.FromObject("second.skill", new { }),
            SkillResult.Succeeded("Second."));
        service.Record(
            SkillPlan.FromObject("third.skill", new { }),
            SkillResult.Succeeded("Third."));

        var recent = service.GetRecent();

        recent.Select(entry => entry.SkillId).Should().Equal("third.skill", "second.skill");
    }

    [Fact]
    public void Record_WhenResultHasShellCommandData_ExposesNormalizedRuntimeFields()
    {
        var service = new InMemorySkillExecutionHistoryService();
        var shellData = new ShellCommandResult
        {
            Command = "pwsh -NoProfile -Command \"Write-Error fail\"",
            WorkingDirectory = "C:\\repo",
            ExitCode = 1,
            StdOut = "normal output",
            StdErr = "failure output",
            TimedOut = true,
            Truncated = true,
            DurationMilliseconds = 1500
        };

        var entry = service.Record(
            SkillPlan.FromObject("shell.run", new { command = shellData.Command }),
            new SkillResult(false, string.Empty, "Command failed.", shellData)
            {
                Status = SkillExecutionStatus.TimedOut,
                ErrorCode = "shell_timeout",
                DurationMilliseconds = 1500
            });

        entry.SkillId.Should().Be("shell.run");
        entry.Status.Should().Be(SkillExecutionStatus.TimedOut);
        entry.ErrorCode.Should().Be("shell_timeout");
        entry.ResultSummary.Should().Be("Command failed.");
        entry.Command.Should().Be(shellData.Command);
        entry.WorkingDirectory.Should().Be("C:\\repo");
        entry.ExitCode.Should().Be(1);
        entry.StdOut.Should().Be("normal output");
        entry.StdErr.Should().Be("failure output");
        entry.TimedOut.Should().BeTrue();
        entry.Truncated.Should().BeTrue();
    }

    [Fact]
    public void Record_WhenPlanCanBeReplayed_StoresReplayPlanJson()
    {
        var service = new InMemorySkillExecutionHistoryService();

        var entry = service.Record(
            SkillPlan.FromObject("apps.list", new { maxItems = 5 }),
            SkillResult.Succeeded("Listed applications."));

        entry.CanReplay.Should().BeTrue();
        entry.ReplayPlanJson.Should().Contain("apps.list");
        entry.ReplayPlanJson.Should().Contain("maxItems");
        entry.ReplayBlockedReason.Should().BeEmpty();
    }

    [Fact]
    public void Record_WhenPlanRequiresConfirmation_BlocksReplay()
    {
        var service = new InMemorySkillExecutionHistoryService();
        var plan = SkillPlan.FromObject("file.patch", new { filePath = "notes.txt", patch = "diff" });
        plan.RequiresConfirmation = true;

        var entry = service.Record(plan, SkillResult.Succeeded("Patched."));

        entry.CanReplay.Should().BeFalse();
        entry.ReplayPlanJson.Should().Contain("file.patch");
        entry.ReplayBlockedReason.Should().Contain("confirmation");
    }

    [Fact]
    public void Record_WhenPlanIsWriteAction_BlocksReplay()
    {
        var service = new InMemorySkillExecutionHistoryService();

        var entry = service.Record(
            SkillPlan.FromObject("files.create", new { filePath = "notes.txt", content = "hello" }),
            SkillResult.Succeeded("Created."));

        entry.CanReplay.Should().BeFalse();
        entry.ReplayPlanJson.Should().Contain("files.create");
        entry.ReplayBlockedReason.Should().Contain("write");
    }
}
