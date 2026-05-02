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
        recent.Single().CanReplay.Should().BeFalse();
        recent.Single().ReplayBlockedReason.Should().Contain("high-risk");
    }

    [Fact]
    public void Record_HighRiskShellRun_BlocksReplay()
    {
        var service = new JsonSkillExecutionHistoryService(_historyFile);

        service.Record(
            SkillPlan.FromObject("shell.run", new { command = "echo hello" }),
            SkillResult.Succeeded("Exit Code: 0"));

        var recent = service.GetRecent().Should().ContainSingle().Subject;
        recent.SkillId.Should().Be("shell.run");
        recent.CanReplay.Should().BeFalse();
        recent.ReplayBlockedReason.Should().Contain("high-risk");
    }

    [Fact]
    public void Record_RedactsSecretsFromPersistedSupportArtifact()
    {
        var service = new JsonSkillExecutionHistoryService(_historyFile);
        var shellResult = new ShellCommandResult
        {
            Command = "curl -H \"Authorization: Bearer abc123\" https://example.test?api_key=secret",
            StdOut = "stdout contains password=secret",
            StdErr = "stderr contains sk-test-secret",
            DurationMilliseconds = 10
        };

        service.Record(
            SkillPlan.FromObject("shell.run", new
            {
                command = "curl https://example.test?api_key=secret",
                apiKey = "sk-test-secret"
            }),
            SkillResult.Succeeded(
                "Completed with Bearer abc123 and password=secret.",
                shellResult));

        var persisted = File.ReadAllText(_historyFile);
        var recent = service.GetRecent().Should().ContainSingle().Subject;

        persisted.Should().NotContain("sk-test-secret");
        persisted.Should().NotContain("Bearer abc123");
        persisted.Should().NotContain("password=secret");
        persisted.Should().NotContain("api_key=secret");
        persisted.Should().Contain("[redacted]");
        recent.ResultSummary.Should().Contain("[redacted]");
        recent.StdOut.Should().Contain("[redacted]");
        recent.StdErr.Should().Contain("[redacted]");
        recent.ReplayPlanJson.Should().Contain("[redacted]");
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
