using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Execution;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Execution;

public sealed class JsonSkillExecutionHistoryServiceTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _historyFile;

    public JsonSkillExecutionHistoryServiceTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-skill-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
        _historyFile = Path.Combine(_workspace, "skill-history.jsonl");
    }

    [Fact]
    public void Record_AppendsJsonLineAndGetRecentReadsNewestFirst()
    {
        var service = new JsonSkillExecutionHistoryService(_historyFile);
        var changedCount = 0;
        service.Changed += (_, _) => changedCount++;

        service.Record(
            SkillPlan.FromObject("apps.list", new { }),
            SkillResult.Succeeded("Listed applications."),
            new DateTimeOffset(2026, 4, 27, 10, 0, 0, TimeSpan.Zero));
        service.Record(
            SkillPlan.FromObject("shell.run", new { command = "git status" }),
            SkillResult.Failed("Command failed.", SkillExecutionStatus.Failed, "shell_exit_code"),
            new DateTimeOffset(2026, 4, 27, 10, 1, 0, TimeSpan.Zero));

        File.ReadAllLines(_historyFile).Should().HaveCount(2);
        var recent = service.GetRecent(1);

        changedCount.Should().Be(2);
        recent.Should().ContainSingle();
        recent.Single().SkillId.Should().Be("shell.run");
        recent.Single().ErrorCode.Should().Be("shell_exit_code");
        recent.Single().ArgumentsSummary.Should().Contain("command=git status");
        recent.Single().ReplayPlanJson.Should().Contain("shell.run");
        recent.Single().CanReplay.Should().BeTrue();
    }

    [Fact]
    public void Clear_RemovesPersistedEntriesAndRaisesChanged()
    {
        var service = new JsonSkillExecutionHistoryService(_historyFile);
        var changedCount = 0;
        service.Changed += (_, _) => changedCount++;

        service.Record(
            SkillPlan.FromObject("apps.list", new { }),
            SkillResult.Succeeded("Listed applications."));
        service.Clear();

        changedCount.Should().Be(2);
        File.Exists(_historyFile).Should().BeFalse();
        service.GetRecent().Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }
}
