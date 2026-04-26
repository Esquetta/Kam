using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Execution;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Execution;

public class SkillExecutionPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_MissingRequiredArgument_ReturnsValidationFailureWithoutCallingExecutor()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "apps.open",
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
        var executor = new RecordingSkillExecutor("apps.open", (_, _) => Task.FromResult(SkillResult.Succeeded("Opened.")));
        var pipeline = new SkillExecutionPipeline(registry, [executor]);

        var result = await pipeline.ExecuteAsync(SkillPlan.FromObject("apps.open", new { }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        result.ErrorMessage.Should().Contain("applicationName");
        executor.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_TypeMismatch_ReturnsValidationFailure()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "system.volume.set",
            Enabled = true,
            Arguments =
            [
                new SkillArgumentDefinition
                {
                    Name = "level",
                    Type = SkillArgumentType.Number,
                    Required = true
                }
            ]
        });
        var executor = new RecordingSkillExecutor("system.volume.set", (_, _) => Task.FromResult(SkillResult.Succeeded("Volume set.")));
        var pipeline = new SkillExecutionPipeline(registry, [executor]);

        var result = await pipeline.ExecuteAsync(SkillPlan.FromObject("system.volume.set", new { level = "loud" }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        result.ErrorMessage.Should().Contain("level");
        executor.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutorThrows_ReturnsNormalizedFailure()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "files.read",
            Enabled = true
        });
        var executor = new RecordingSkillExecutor(
            "files.read",
            (_, _) => throw new InvalidOperationException("Access denied."));
        var pipeline = new SkillExecutionPipeline(registry, [executor]);

        var result = await pipeline.ExecuteAsync(SkillPlan.FromObject("files.read", new { path = "notes.txt" }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.Failed);
        result.ErrorMessage.Should().Contain("Access denied");
    }

    [Fact]
    public async Task ExecuteAsync_ExecutorExceedsTimeout_ReturnsTimeoutFailure()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "slow.skill",
            Enabled = true,
            TimeoutMilliseconds = 25
        });
        var executor = new RecordingSkillExecutor(
            "slow.skill",
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return SkillResult.Succeeded("Too late.");
            });
        var pipeline = new SkillExecutionPipeline(registry, [executor]);

        var result = await pipeline.ExecuteAsync(SkillPlan.FromObject("slow.skill", new { }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.TimedOut);
        result.ErrorMessage.Should().Contain("timed out");
        result.DurationMilliseconds.Should().BeGreaterThan(0);
        result.DurationMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessWithEmptyMessage_ReturnsNormalizedSuccessMessage()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "apps.list",
            Enabled = true
        });
        var executor = new RecordingSkillExecutor("apps.list", (_, _) => Task.FromResult(SkillResult.Succeeded(string.Empty)));
        var pipeline = new SkillExecutionPipeline(registry, [executor]);

        var result = await pipeline.ExecuteAsync(SkillPlan.FromObject("apps.list", new { }));

        result.Success.Should().BeTrue();
        result.Status.Should().Be(SkillExecutionStatus.Succeeded);
        result.Message.Should().Be("Skill apps.list completed.");
        result.DurationMilliseconds.Should().BeGreaterThanOrEqualTo(0);
    }

    private static InMemorySkillRegistry CreateRegistry(KamSkillManifest manifest)
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(manifest);
        return registry;
    }

    private sealed class RecordingSkillExecutor : ISkillExecutor
    {
        private readonly string _skillId;
        private readonly Func<SkillPlan, CancellationToken, Task<SkillResult>> _execute;

        public RecordingSkillExecutor(
            string skillId,
            Func<SkillPlan, CancellationToken, Task<SkillResult>> execute)
        {
            _skillId = skillId;
            _execute = execute;
        }

        public int CallCount { get; private set; }

        public bool CanExecute(string skillId)
        {
            return _skillId.Equals(skillId, StringComparison.OrdinalIgnoreCase);
        }

        public Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _execute(plan, cancellationToken);
        }
    }
}
