using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public class SkillConfirmationServiceTests
{
    [Fact]
    public void Queue_AddsPendingRequestAndRaisesChange()
    {
        var pipeline = new RecordingSkillExecutionPipeline(_ => SkillResult.Succeeded("Deleted."));
        var service = CreateService(pipeline);
        var plan = SkillPlan.FromObject("files.delete", new { filePath = "C:\\temp\\notes.txt" });
        var changeCount = 0;
        service.PendingChanged += (_, _) => changeCount++;

        var request = service.Queue("delete notes", plan);

        request.Id.Should().NotBeEmpty();
        request.UserCommand.Should().Be("delete notes");
        request.Plan.Should().BeSameAs(plan);
        request.SkillId.Should().Be("files.delete");
        service.GetPending().Should().ContainSingle().Which.Should().BeSameAs(request);
        changeCount.Should().Be(1);
    }

    [Fact]
    public async Task ApproveAsync_ExecutesPlanAndRemovesPendingRequest()
    {
        var pipeline = new RecordingSkillExecutionPipeline(_ => SkillResult.Succeeded("Deleted."));
        var service = CreateService(pipeline);
        var plan = SkillPlan.FromObject("files.delete", new { filePath = "C:\\temp\\notes.txt" });
        var request = service.Queue("delete notes", plan);

        var result = await service.ApproveAsync(request.Id);

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Deleted.");
        pipeline.CallCount.Should().Be(1);
        pipeline.LastPlan.Should().BeSameAs(plan);
        service.GetPending().Should().BeEmpty();
    }

    [Fact]
    public void Reject_RemovesPendingRequestWithoutExecutingPlan()
    {
        var pipeline = new RecordingSkillExecutionPipeline(_ => SkillResult.Succeeded("Should not run."));
        var service = CreateService(pipeline);
        var plan = SkillPlan.FromObject("files.delete", new { filePath = "C:\\temp\\notes.txt" });
        var request = service.Queue("delete notes", plan);

        var removed = service.Reject(request.Id);

        removed.Should().BeTrue();
        service.GetPending().Should().BeEmpty();
        pipeline.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ApproveAsync_MissingRequestReturnsValidationFailure()
    {
        var pipeline = new RecordingSkillExecutionPipeline(_ => SkillResult.Succeeded("Should not run."));
        var service = CreateService(pipeline);

        var result = await service.ApproveAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        result.ErrorCode.Should().Be("confirmation_not_found");
        pipeline.CallCount.Should().Be(0);
    }

    private static ISkillConfirmationService CreateService(ISkillExecutionPipeline pipeline)
    {
        var services = new ServiceCollection();
        services.AddSingleton(pipeline);
        var provider = services.BuildServiceProvider();

        return new SkillConfirmationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SkillConfirmationService>.Instance);
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
