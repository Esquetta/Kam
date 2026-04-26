using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Audit;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Audit;

public sealed class JsonSkillAuditLogServiceTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _auditFile;

    public JsonSkillAuditLogServiceTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-skill-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
        _auditFile = Path.Combine(_workspace, "skill-audit.jsonl");
    }

    [Fact]
    public async Task RecordAsync_AppendsJsonLineAndGetRecentReadsNewestFirst()
    {
        var service = new JsonSkillAuditLogService(_auditFile);

        await service.RecordAsync(new SkillAuditRecord
        {
            SkillId = "local.desktop-navigation",
            ExecutorType = "local",
            ModelId = "openrouter/test-model",
            UserInput = "type hello",
            ActionTypes = [SkillActionTypes.TypeText],
            RequiredPermissions = [SkillPermission.ProcessControl],
            MissingPermissions = [SkillPermission.ProcessControl],
            Status = SkillExecutionStatus.ReviewRequired,
            ErrorCode = "action_confirmation_required",
            ResultMessage = "Needs confirmation."
        });
        await service.RecordAsync(new SkillAuditRecord
        {
            SkillId = "local.desktop-navigation",
            ExecutorType = "local",
            ModelId = "openrouter/test-model",
            UserInput = "type hello",
            ActionTypes = [SkillActionTypes.TypeText],
            RequiredPermissions = [SkillPermission.ProcessControl],
            MissingPermissions = [SkillPermission.ProcessControl],
            Status = SkillExecutionStatus.Succeeded,
            ResultMessage = "Typed hello."
        });

        File.ReadAllLines(_auditFile).Should().HaveCount(2);
        var recent = await service.GetRecentAsync(1);

        recent.Should().ContainSingle();
        recent.Single().Status.Should().Be(SkillExecutionStatus.Succeeded);
        recent.Single().ModelId.Should().Be("openrouter/test-model");
        recent.Single().ActionTypes.Should().ContainSingle().Which.Should().Be(SkillActionTypes.TypeText);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }
}
