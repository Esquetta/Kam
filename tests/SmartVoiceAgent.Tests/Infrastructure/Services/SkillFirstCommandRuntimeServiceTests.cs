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
        var confirmation = new RecordingSkillConfirmationService();
        var runtime = CreateRuntime(planner, pipeline, confirmation);

        var result = await runtime.ExecuteAsync("list installed apps");

        result.Success.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.ConfirmationId.Should().BeNull();
        result.Message.Should().Be("Listed applications.");
        result.SkillId.Should().Be("apps.list");
        result.Status.Should().Be(SkillExecutionStatus.Succeeded);
        pipeline.CallCount.Should().Be(1);
        pipeline.LastPlan.Should().BeSameAs(plan);
        confirmation.QueueCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPlan_ReturnsFailureWithoutRunningPipeline()
    {
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Failure("Model returned markdown."));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Should not run."));
        var confirmation = new RecordingSkillConfirmationService();
        var runtime = CreateRuntime(planner, pipeline, confirmation);

        var result = await runtime.ExecuteAsync("open notepad");

        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse();
        result.ConfirmationId.Should().BeNull();
        result.Message.Should().Contain("Model returned markdown");
        result.ErrorCode.Should().Be("planner_invalid");
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        pipeline.CallCount.Should().Be(0);
        confirmation.QueueCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_PlanRequiresConfirmation_QueuesRequestWithoutRunningPipeline()
    {
        var plan = SkillPlan.FromObject("files.delete", new { filePath = "C:\\temp\\notes.txt" });
        plan.RequiresConfirmation = true;
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Should not run."));
        var confirmation = new RecordingSkillConfirmationService();
        var runtime = CreateRuntime(planner, pipeline, confirmation);

        var result = await runtime.ExecuteAsync("delete notes");

        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationId.Should().Be(confirmation.LastRequest?.Id);
        result.SkillId.Should().Be("files.delete");
        result.Message.Should().Contain("requires confirmation");
        result.ErrorCode.Should().Be("confirmation_required");
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        pipeline.CallCount.Should().Be(0);
        confirmation.QueueCount.Should().Be(1);
        confirmation.LastRequest.Should().NotBeNull();
        confirmation.LastRequest!.UserCommand.Should().Be("delete notes");
        confirmation.LastRequest.Plan.Should().BeSameAs(plan);
    }

    [Fact]
    public async Task ExecuteAsync_HighRiskSkill_QueuesRequestWithoutRunningPipeline()
    {
        var plan = SkillPlan.FromObject("files.delete", new { filePath = "C:\\temp\\notes.txt" });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Should not run."));
        var confirmation = new RecordingSkillConfirmationService();
        var registry = new StubSkillRegistry(
            new KamSkillManifest
            {
                Id = "files.delete",
                DisplayName = "Delete File",
                Enabled = true,
                RiskLevel = SkillRiskLevel.High
            });
        var runtime = CreateRuntime(planner, pipeline, confirmation, registry);

        var result = await runtime.ExecuteAsync("delete notes");

        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationId.Should().Be(confirmation.LastRequest?.Id);
        result.SkillId.Should().Be("files.delete");
        result.ErrorCode.Should().Be("confirmation_required");
        pipeline.CallCount.Should().Be(0);
        confirmation.QueueCount.Should().Be(1);
    }

    private static ICommandRuntimeService CreateRuntime(
        ISkillPlannerService planner,
        ISkillExecutionPipeline pipeline,
        ISkillConfirmationService confirmation,
        ISkillRegistry? registry = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(planner);
        services.AddSingleton(pipeline);
        services.AddSingleton(confirmation);
        services.AddSingleton(registry ?? new StubSkillRegistry());
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

    private sealed class RecordingSkillConfirmationService : ISkillConfirmationService
    {
        public event EventHandler? PendingChanged;

        public int QueueCount { get; private set; }

        public SkillConfirmationRequest? LastRequest { get; private set; }

        public IReadOnlyCollection<SkillConfirmationRequest> GetPending() =>
            LastRequest is null ? [] : [LastRequest];

        public SkillConfirmationRequest Queue(string userCommand, SkillPlan plan)
        {
            QueueCount++;
            LastRequest = new SkillConfirmationRequest
            {
                Id = Guid.NewGuid(),
                UserCommand = userCommand,
                Plan = plan,
                CreatedAt = DateTimeOffset.UtcNow
            };
            PendingChanged?.Invoke(this, EventArgs.Empty);
            return LastRequest;
        }

        public Task<SkillResult> ApproveAsync(
            Guid requestId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SkillResult.Failed(
                "Not implemented by test stub.",
                SkillExecutionStatus.Failed,
                "test_stub"));
        }

        public bool Reject(Guid requestId) => false;
    }

    private sealed class StubSkillRegistry : ISkillRegistry
    {
        private readonly Dictionary<string, KamSkillManifest> _manifests;

        public StubSkillRegistry(params KamSkillManifest[] manifests)
        {
            _manifests = manifests.ToDictionary(manifest => manifest.Id);
        }

        public void Register(KamSkillManifest manifest)
        {
            _manifests[manifest.Id] = manifest;
        }

        public bool TryGet(string skillId, out KamSkillManifest? manifest) =>
            _manifests.TryGetValue(skillId, out manifest);

        public IReadOnlyCollection<KamSkillManifest> GetAll() => _manifests.Values.ToArray();
    }
}
