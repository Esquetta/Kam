using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Evaluation;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Evaluation;

public class SkillEvalHarnessTests
{
    [Fact]
    public async Task RunAsync_ExecutesCasesAndSummarizesPassFailures()
    {
        var pipeline = new StubSkillExecutionPipeline(plan =>
            plan.SkillId == "ok.skill"
                ? SkillResult.Succeeded("OK.")
                : SkillResult.Failed(
                    "Missing argument.",
                    SkillExecutionStatus.ValidationFailed,
                    "validation_failed"));
        var harness = new SkillEvalHarness(pipeline);

        var summary = await harness.RunAsync(
        [
            new SkillEvalCase
            {
                Name = "successful path",
                Plan = SkillPlan.FromObject("ok.skill", new { }),
                ExpectedStatus = SkillExecutionStatus.Succeeded
            },
            new SkillEvalCase
            {
                Name = "failing path",
                Plan = SkillPlan.FromObject("bad.skill", new { }),
                ExpectedStatus = SkillExecutionStatus.Succeeded
            }
        ]);

        summary.Total.Should().Be(2);
        summary.Passed.Should().Be(1);
        summary.Failed.Should().Be(1);
        summary.Results.Should().HaveCount(2);
        summary.Results[0].Passed.Should().BeTrue();
        summary.Results[0].SkillId.Should().Be("ok.skill");
        summary.Results[1].Passed.Should().BeFalse();
        summary.Results[1].ActualStatus.Should().Be(SkillExecutionStatus.ValidationFailed);
        summary.Results[1].Message.Should().Contain("Expected");
    }

    private sealed class StubSkillExecutionPipeline : ISkillExecutionPipeline
    {
        private readonly Func<SkillPlan, SkillResult> _execute;

        public StubSkillExecutionPipeline(Func<SkillPlan, SkillResult> execute)
        {
            _execute = execute;
        }

        public Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_execute(plan));
        }
    }
}
