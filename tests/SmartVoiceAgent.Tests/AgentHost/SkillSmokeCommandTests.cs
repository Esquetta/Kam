using FluentAssertions;
using SmartVoiceAgent.AgentHost.ConsoleApp;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Tests.AgentHost;

public sealed class SkillSmokeCommandTests
{
    [Fact]
    public async Task RunAsync_AllCasesPass_WritesSummaryAndReturnsSuccess()
    {
        var summaryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "skill-smoke.md");
        var command = new SkillSmokeCommand(
            new RecordingHarness(new SkillEvalSummary
            {
                Total = 1,
                Passed = 1,
                Failed = 0,
                Results =
                [
                    Result("apps.list", passed: true)
                ]
            }),
            new StaticCatalog([Case("apps.list")]));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new SkillSmokeOptions { SummaryPath = summaryPath },
            output,
            error);

        exitCode.Should().Be(0);
        File.Exists(summaryPath).Should().BeTrue();
        var markdown = await File.ReadAllTextAsync(summaryPath);
        markdown.Should().Contain("# Skill Smoke");
        markdown.Should().Contain("- status: completed");
        markdown.Should().Contain("- failed: 0");
        markdown.Should().Contain("[PASS] `apps.list`");
        output.ToString().Should().Contain("Skill smoke completed: 1/1 passed");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_FailedCase_WritesFailureAndReturnsError()
    {
        var command = new SkillSmokeCommand(
            new RecordingHarness(new SkillEvalSummary
            {
                Total = 1,
                Passed = 0,
                Failed = 1,
                Results =
                [
                    Result("files.read", passed: false, message: "Expected Succeeded, got Failed.")
                ]
            }),
            new StaticCatalog([Case("files.read")]));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(new SkillSmokeOptions(), output, error);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("[FAIL] files.read");
        error.ToString().Should().Contain("Skill smoke failed: 1/1 failed");
    }

    [Fact]
    public async Task RunAsync_WithSkillFilter_PassesOnlyMatchingCasesToHarness()
    {
        var harness = new RecordingHarness(new SkillEvalSummary
        {
            Total = 1,
            Passed = 1,
            Failed = 0,
            Results =
            [
                Result("shell.run", passed: true)
            ]
        });
        var command = new SkillSmokeCommand(
            harness,
            new StaticCatalog([Case("apps.list"), Case("shell.run")]));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await command.RunAsync(
            new SkillSmokeOptions { SkillIds = ["shell.run"] },
            output,
            error);

        exitCode.Should().Be(0);
        harness.ReceivedCases.Should().ContainSingle();
        harness.ReceivedCases[0].Plan.SkillId.Should().Be("shell.run");
    }

    private static SkillEvalCase Case(string skillId)
    {
        return new SkillEvalCase
        {
            Name = $"{skillId} smoke",
            Plan = SkillPlan.FromObject(skillId, new { }),
            ExpectedStatus = SkillExecutionStatus.Succeeded
        };
    }

    private static SkillEvalResult Result(
        string skillId,
        bool passed,
        string message = "ok")
    {
        return new SkillEvalResult
        {
            Name = $"{skillId} smoke",
            SkillId = skillId,
            ExpectedStatus = SkillExecutionStatus.Succeeded,
            ActualStatus = passed ? SkillExecutionStatus.Succeeded : SkillExecutionStatus.Failed,
            Passed = passed,
            DurationMilliseconds = 12,
            Message = message
        };
    }

    private sealed class StaticCatalog : ISkillEvalCaseCatalog
    {
        private readonly IReadOnlyCollection<SkillEvalCase> _cases;

        public StaticCatalog(IReadOnlyCollection<SkillEvalCase> cases)
        {
            _cases = cases;
        }

        public IReadOnlyCollection<SkillEvalCase> CreateSmokeCases() => _cases;
    }

    private sealed class RecordingHarness : ISkillEvalHarness
    {
        private readonly SkillEvalSummary _summary;

        public RecordingHarness(SkillEvalSummary summary)
        {
            _summary = summary;
        }

        public IReadOnlyList<SkillEvalCase> ReceivedCases { get; private set; } = [];

        public Task<SkillEvalSummary> RunAsync(
            IEnumerable<SkillEvalCase> cases,
            CancellationToken cancellationToken = default)
        {
            ReceivedCases = cases.ToArray();
            return Task.FromResult(_summary);
        }
    }
}
