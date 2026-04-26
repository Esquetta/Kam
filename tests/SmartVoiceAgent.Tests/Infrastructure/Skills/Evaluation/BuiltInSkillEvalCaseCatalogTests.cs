using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;
using SmartVoiceAgent.Infrastructure.Skills.Evaluation;
using SmartVoiceAgent.Infrastructure.Skills.Execution;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Evaluation;

public class BuiltInSkillEvalCaseCatalogTests
{
    [Fact]
    public void CreateSmokeCases_CoversMigratedBuiltInSkillFamilies()
    {
        var catalog = new BuiltInSkillEvalCaseCatalog();

        var cases = catalog.CreateSmokeCases();

        cases.Should().Contain(testCase => testCase.Plan.SkillId.StartsWith("apps."));
        cases.Should().Contain(testCase => testCase.Plan.SkillId.StartsWith("files."));
        cases.Should().Contain(testCase => testCase.Plan.SkillId.StartsWith("system."));
        cases.Should().Contain(testCase => testCase.Plan.SkillId.StartsWith("web."));
        cases.Should().Contain(testCase => testCase.Plan.SkillId.StartsWith("communication."));
        cases.Should().Contain(testCase => testCase.Plan.SkillId.StartsWith("clipboard."));
        cases.Should().Contain(testCase => testCase.Plan.SkillId.StartsWith("workspace."));
        cases.Should().Contain(testCase => testCase.Plan.SkillId.StartsWith("code."));
    }

    [Fact]
    public async Task CreateSmokeCases_AllCasesSatisfyBuiltInManifestValidation()
    {
        var registry = new InMemorySkillRegistry();
        foreach (var manifest in BuiltInSkillManifestCatalog.CreateAll())
        {
            registry.Register(manifest);
        }

        var pipeline = new SkillExecutionPipeline(registry, [new UniversalSuccessSkillExecutor()]);
        var harness = new SkillEvalHarness(pipeline);
        var catalog = new BuiltInSkillEvalCaseCatalog();

        var summary = await harness.RunAsync(catalog.CreateSmokeCases());

        summary.Failed.Should().Be(0);
        summary.Passed.Should().Be(summary.Total);
    }

    private sealed class UniversalSuccessSkillExecutor : ISkillExecutor
    {
        public bool CanExecute(string skillId)
        {
            return true;
        }

        public Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SkillResult.Succeeded($"Validated {plan.SkillId}."));
        }
    }
}
