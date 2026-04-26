using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Evaluation;

public sealed class SkillEvalHarness : ISkillEvalHarness
{
    private readonly ISkillExecutionPipeline _pipeline;

    public SkillEvalHarness(ISkillExecutionPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<SkillEvalSummary> RunAsync(
        IEnumerable<SkillEvalCase> cases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var results = new List<SkillEvalResult>();

        foreach (var testCase in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _pipeline.ExecuteAsync(testCase.Plan, cancellationToken);
            var passed = result.Status == testCase.ExpectedStatus;

            results.Add(new SkillEvalResult
            {
                Name = testCase.Name,
                SkillId = testCase.Plan.SkillId,
                ExpectedStatus = testCase.ExpectedStatus,
                ActualStatus = result.Status,
                Passed = passed,
                DurationMilliseconds = result.DurationMilliseconds,
                Message = passed
                    ? GetResultMessage(result)
                    : $"Expected {testCase.ExpectedStatus}, got {result.Status}. {GetResultMessage(result)}"
            });
        }

        var passedCount = results.Count(result => result.Passed);

        return new SkillEvalSummary
        {
            Total = results.Count,
            Passed = passedCount,
            Failed = results.Count - passedCount,
            Results = results
        };
    }

    private static string GetResultMessage(SkillResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            return result.Message;
        }

        return !string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? result.ErrorMessage
            : "No result message.";
    }
}
