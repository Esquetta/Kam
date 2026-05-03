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
    public async Task ExecuteAsync_UnknownSkillPlan_ReturnsFailureWithoutRunningPipeline()
    {
        var plan = SkillPlan.FromObject("unknown.skill", new { });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Should not run."));
        var confirmation = new RecordingSkillConfirmationService();
        var runtime = CreateRuntime(planner, pipeline, confirmation);

        var result = await runtime.ExecuteAsync("run unknown skill");

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        result.ErrorCode.Should().Be("planner_invalid");
        result.Message.Should().Contain("unknown skill");
        pipeline.CallCount.Should().Be(0);
        confirmation.QueueCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRequiredArgument_ReturnsFailureWithoutRunningPipeline()
    {
        var plan = SkillPlan.FromObject("apps.open", new { });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Should not run."));
        var confirmation = new RecordingSkillConfirmationService();
        var registry = new StubSkillRegistry(
            new KamSkillManifest
            {
                Id = "apps.open",
                DisplayName = "Open Application",
                Enabled = true,
                Arguments =
                [
                    new SkillArgumentDefinition
                    {
                        Name = "applicationName",
                        Type = SkillArgumentType.String,
                        Required = true
                    }
                ]
            });
        var runtime = CreateRuntime(planner, pipeline, confirmation, registry);

        var result = await runtime.ExecuteAsync("open app");

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        result.ErrorCode.Should().Be("planner_invalid");
        result.Message.Should().Contain("applicationName");
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
    public async Task ExecuteAsync_PlannerRequiresConfirmationForGrantedAppOpen_RunsPipeline()
    {
        var plan = SkillPlan.FromObject("apps.open", new { applicationName = "Spotify" });
        plan.RequiresConfirmation = true;
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Opened Spotify."));
        var confirmation = new RecordingSkillConfirmationService();
        var registry = new StubSkillRegistry(
            new KamSkillManifest
            {
                Id = "apps.open",
                DisplayName = "Open Application",
                Enabled = true,
                RiskLevel = SkillRiskLevel.Medium,
                Permissions = [SkillPermission.ProcessLaunch],
                GrantedPermissions = [SkillPermission.ProcessLaunch]
            });
        var runtime = CreateRuntime(planner, pipeline, confirmation, registry);

        var result = await runtime.ExecuteAsync("Open Spotify");

        result.Success.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.Message.Should().Be("Opened Spotify.");
        result.SkillId.Should().Be("apps.open");
        pipeline.CallCount.Should().Be(1);
        confirmation.QueueCount.Should().Be(0);
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

    [Theory]
    [InlineData("file.patch")]
    [InlineData("file.replace_range")]
    public async Task ExecuteAsync_HighRiskFileEditSkill_RunsPreviewAndQueuesConfirmationWithPreview(string skillId)
    {
        var plan = SkillPlan.FromObject(
            skillId,
            new
            {
                filePath = "C:\\temp\\notes.txt",
                oldText = "old",
                newText = "new"
            });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(previewPlan =>
        {
            previewPlan.Arguments["previewOnly"].GetBoolean().Should().BeTrue();
            return SkillResult.Succeeded("Diff Preview:\n-old\n+new");
        });
        var confirmation = new RecordingSkillConfirmationService();
        var registry = new StubSkillRegistry(
            new KamSkillManifest
            {
                Id = skillId,
                DisplayName = "Edit File",
                Enabled = true,
                RiskLevel = SkillRiskLevel.High
            });
        var runtime = CreateRuntime(planner, pipeline, confirmation, registry);

        var result = await runtime.ExecuteAsync("patch notes");

        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationId.Should().Be(confirmation.LastRequest?.Id);
        result.SkillId.Should().Be(skillId);
        result.ErrorCode.Should().Be("confirmation_required");
        pipeline.CallCount.Should().Be(1);
        pipeline.LastPlan.Should().NotBeSameAs(plan);
        pipeline.LastPlan!.Arguments["previewOnly"].GetBoolean().Should().BeTrue();
        confirmation.QueueCount.Should().Be(1);
        confirmation.LastRequest.Should().NotBeNull();
        confirmation.LastRequest!.Plan.Should().BeSameAs(plan);
        confirmation.LastRequest.Preview.Should().Be("Diff Preview:\n-old\n+new");
    }

    [Fact]
    public async Task ExecuteAsync_HighRiskFileWriteSkill_RunsPreviewAndQueuesConfirmationWithPreview()
    {
        var plan = SkillPlan.FromObject(
            "files.write",
            new
            {
                filePath = "C:\\temp\\notes.txt",
                content = "new"
            });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(previewPlan =>
        {
            previewPlan.Arguments["previewOnly"].GetBoolean().Should().BeTrue();
            return SkillResult.Succeeded("Diff Preview:\n-old\n+new");
        });
        var confirmation = new RecordingSkillConfirmationService();
        var registry = new StubSkillRegistry(
            new KamSkillManifest
            {
                Id = "files.write",
                DisplayName = "Write File",
                Enabled = true,
                RiskLevel = SkillRiskLevel.High
            });
        var runtime = CreateRuntime(planner, pipeline, confirmation, registry);

        var result = await runtime.ExecuteAsync("write notes");

        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationId.Should().Be(confirmation.LastRequest?.Id);
        result.SkillId.Should().Be("files.write");
        result.ErrorCode.Should().Be("confirmation_required");
        pipeline.CallCount.Should().Be(1);
        pipeline.LastPlan.Should().NotBeSameAs(plan);
        pipeline.LastPlan!.Arguments["previewOnly"].GetBoolean().Should().BeTrue();
        confirmation.QueueCount.Should().Be(1);
        confirmation.LastRequest.Should().NotBeNull();
        confirmation.LastRequest!.Plan.Should().BeSameAs(plan);
        confirmation.LastRequest.Preview.Should().Be("Diff Preview:\n-old\n+new");
    }

    [Fact]
    public async Task ExecuteAsync_FilePatchPreviewFailure_ReturnsFailureWithoutQueueingConfirmation()
    {
        var plan = SkillPlan.FromObject(
            "file.patch",
            new
            {
                filePath = "C:\\temp\\notes.txt",
                oldText = "missing",
                newText = "new"
            });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(_ => SkillResult.Failed(
            "oldText was not found.",
            SkillExecutionStatus.ValidationFailed,
            "old_text_not_found"));
        var confirmation = new RecordingSkillConfirmationService();
        var registry = new StubSkillRegistry(
            new KamSkillManifest
            {
                Id = "file.patch",
                DisplayName = "Patch File",
                Enabled = true,
                RiskLevel = SkillRiskLevel.High
            });
        var runtime = CreateRuntime(planner, pipeline, confirmation, registry);

        var result = await runtime.ExecuteAsync("patch notes");

        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse();
        result.ConfirmationId.Should().BeNull();
        result.SkillId.Should().Be("file.patch");
        result.Message.Should().Be("oldText was not found.");
        result.ErrorCode.Should().Be("old_text_not_found");
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        pipeline.CallCount.Should().Be(1);
        confirmation.QueueCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_FilePatchPreviewOnly_RunsPipelineWithoutConfirmation()
    {
        var plan = SkillPlan.FromObject(
            "file.patch",
            new
            {
                filePath = "C:\\temp\\notes.txt",
                oldText = "old",
                newText = "new",
                previewOnly = true
            });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Succeeded("Diff Preview:\n-old\n+new"));
        var confirmation = new RecordingSkillConfirmationService();
        var registry = new StubSkillRegistry(
            new KamSkillManifest
            {
                Id = "file.patch",
                DisplayName = "Patch File",
                Enabled = true,
                RiskLevel = SkillRiskLevel.High
            });
        var runtime = CreateRuntime(planner, pipeline, confirmation, registry);

        var result = await runtime.ExecuteAsync("preview patch notes");

        result.Success.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.Message.Should().Be("Diff Preview:\n-old\n+new");
        pipeline.CallCount.Should().Be(1);
        pipeline.LastPlan.Should().BeSameAs(plan);
        confirmation.QueueCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ActionConfirmationRequiredResult_QueuesOriginalPlan()
    {
        var plan = SkillPlan.FromObject("local.desktop-navigation", new { input = "type hello" });
        var planner = new StubSkillPlannerService(SkillPlanParseResult.Success(plan));
        var pipeline = new RecordingSkillExecutionPipeline(
            _ => SkillResult.Failed(
                "Skill wants to run actions: type_text. Missing permissions: ProcessControl.",
                SkillExecutionStatus.ReviewRequired,
                "action_confirmation_required"));
        var confirmation = new RecordingSkillConfirmationService();
        var runtime = CreateRuntime(planner, pipeline, confirmation);

        var result = await runtime.ExecuteAsync("type hello");

        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationId.Should().Be(confirmation.LastRequest?.Id);
        result.SkillId.Should().Be("local.desktop-navigation");
        result.ErrorCode.Should().Be("confirmation_required");
        pipeline.CallCount.Should().Be(1);
        confirmation.QueueCount.Should().Be(1);
        confirmation.LastRequest.Should().NotBeNull();
        confirmation.LastRequest!.Plan.Should().BeSameAs(plan);
        confirmation.LastRequest.Reason.Should().Contain("type_text");
        confirmation.LastRequest.Reason.Should().Contain("ProcessControl");
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
        services.AddSingleton(registry ?? CreateDefaultRegistry());
        var provider = services.BuildServiceProvider();

        return new SkillFirstCommandRuntimeService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SkillFirstCommandRuntimeService>.Instance);
    }

    private static StubSkillRegistry CreateDefaultRegistry()
    {
        return new StubSkillRegistry(
            new KamSkillManifest
            {
                Id = "apps.list",
                DisplayName = "List Applications",
                Enabled = true
            },
            new KamSkillManifest
            {
                Id = "files.delete",
                DisplayName = "Delete File",
                Enabled = true,
                RiskLevel = SkillRiskLevel.High
            },
            new KamSkillManifest
            {
                Id = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Enabled = true
            });
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

        public SkillConfirmationRequest Queue(
            string userCommand,
            SkillPlan plan,
            string? reason = null,
            string? preview = null)
        {
            QueueCount++;
            LastRequest = new SkillConfirmationRequest
            {
                Id = Guid.NewGuid(),
                UserCommand = userCommand,
                Plan = plan,
                CreatedAt = DateTimeOffset.UtcNow,
                Reason = reason ?? string.Empty,
                Preview = preview ?? string.Empty
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
