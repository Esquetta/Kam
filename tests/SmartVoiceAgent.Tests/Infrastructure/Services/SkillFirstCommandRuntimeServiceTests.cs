using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Commands;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public class SkillFirstCommandRuntimeServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ValidPlan_RunsSkillPipeline()
    {
        var plan = SkillPlan.FromObject("apps.list", new { });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Listed applications."));
        var runtime = CreateRuntime(planner, pipeline);

        var result = await runtime.ExecuteAsync("list installed apps");

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Listed applications.");
        result.SkillId.Should().Be("apps.list");
        result.Status.Should().Be(SkillExecutionStatus.Succeeded);
        pipeline.CallCount.Should().Be(1);
        pipeline.LastPlan.Should().BeSameAs(plan);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPlan_ReturnsFailureWithoutRunningPipeline()
    {
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Failure("Model returned markdown."));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Should not run."));
        var runtime = CreateRuntime(planner, pipeline);

        var result = await runtime.ExecuteAsync("open notepad");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Model returned markdown");
        result.ErrorCode.Should().Be("planner_invalid");
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        pipeline.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_PlanRequiresConfirmation_ReturnsFailureWithoutRunningPipeline()
    {
        var plan = SkillPlan.FromObject("files.delete", new { filePath = "C:\\temp\\notes.txt" });
        plan.RequiresConfirmation = true;
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Should not run."));
        var runtime = CreateRuntime(planner, pipeline);

        var result = await runtime.ExecuteAsync("delete notes");

        result.Success.Should().BeFalse();
        result.SkillId.Should().Be("files.delete");
        result.Message.Should().Contain("requires confirmation");
        result.ErrorCode.Should().Be("confirmation_required");
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        pipeline.CallCount.Should().Be(0);
    }

    private static ICommandRuntimeService CreateRuntime(
        ISkillPlannerService planner,
        ISkillExecutionPipeline pipeline)
    {
        var services = new ServiceCollection();
        services.AddSingleton(planner);
        services.AddSingleton(pipeline);
        var provider = services.BuildServiceProvider();

        return new SkillFirstCommandRuntimeService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SkillFirstCommandRuntimeService>.Instance);
    }

    private sealed class StubSkillPlannerService : ISkillPlannerService
    {
        private readonly SkillPlanParseResult _result;

        public StubSkillPlannerService(SkillPlanParseResult result)
        {
            _result = result;
        }

        public Task<SkillPlanParseResult> CreatePlanAsync(
            string userRequest,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingSkillExecutionPipeline : ISkillExecutionPipeline
    {
        private readonly Func<SkillPlan, SkillResult> _execute;

        public RecordingSkillExecutionPipeline(Func<SkillPlan, SkillResult> execute)
        {
            _execute = execute;
        }

        public int CallCount { get; private set; }

        public SkillPlan? LastPlan { get; private set; }

        public Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPlan = plan;
            return Task.FromResult(_execute(plan));
        }
    }
}
