using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Evaluation;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Evaluation;

public sealed class SkillTestServiceTests
{
    [Fact]
    public async Task TestAsync_UsesRegisteredSmokeCaseAndRecordsAudit()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "files.exists",
            DisplayName = "Check File Exists",
            Source = "builtin",
            ExecutorType = "builtin",
            Enabled = true
        });
        var pipeline = new RecordingSkillExecutionPipeline(
            SkillResult.Succeeded("File exists.") with { DurationMilliseconds = 42 });
        var auditLog = new RecordingSkillAuditLogService();
        var service = new SkillTestService(
            registry,
            pipeline,
            new StaticSkillEvalCaseCatalog(
            [
                new SkillEvalCase
                {
                    Name = "files.exists smoke",
                    Plan = SkillPlan.FromObject("files.exists", new { filePath = "README.md" })
                }
            ]),
            auditLog);

        var result = await service.TestAsync("files.exists");

        result.Success.Should().BeTrue();
        pipeline.ExecutedPlan.Should().NotBeNull();
        pipeline.ExecutedPlan!.SkillId.Should().Be("files.exists");
        pipeline.ExecutedPlan.Arguments["filePath"].GetString().Should().Be("README.md");
        auditLog.Records.Should().ContainSingle();
        var record = auditLog.Records.Single();
        record.SkillId.Should().Be("files.exists");
        record.ExecutorType.Should().Be("builtin");
        record.UserInput.Should().Be("Skill test action");
        record.Status.Should().Be(SkillExecutionStatus.Succeeded);
        record.ResultMessage.Should().Be("File exists.");
        record.DurationMilliseconds.Should().Be(42);
    }

    private sealed class RecordingSkillExecutionPipeline : ISkillExecutionPipeline
    {
        private readonly SkillResult _result;

        public RecordingSkillExecutionPipeline(SkillResult result)
        {
            _result = result;
        }

        public SkillPlan? ExecutedPlan { get; private set; }

        public Task<SkillResult> ExecuteAsync(
            SkillPlan plan,
            CancellationToken cancellationToken = default)
        {
            ExecutedPlan = plan;
            return Task.FromResult(_result);
        }
    }

    private sealed class StaticSkillEvalCaseCatalog : ISkillEvalCaseCatalog
    {
        private readonly IReadOnlyCollection<SkillEvalCase> _cases;

        public StaticSkillEvalCaseCatalog(IReadOnlyCollection<SkillEvalCase> cases)
        {
            _cases = cases;
        }

        public IReadOnlyCollection<SkillEvalCase> CreateSmokeCases() => _cases;
    }

    private sealed class RecordingSkillAuditLogService : ISkillAuditLogService
    {
        public List<SkillAuditRecord> Records { get; } = [];

        public Task RecordAsync(
            SkillAuditRecord record,
            CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<SkillAuditRecord>> GetRecentAsync(
            int maxCount = 100,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<SkillAuditRecord>>(
                Records
                    .OrderByDescending(record => record.Timestamp)
                    .Take(maxCount)
                    .ToArray());
        }
    }
}
