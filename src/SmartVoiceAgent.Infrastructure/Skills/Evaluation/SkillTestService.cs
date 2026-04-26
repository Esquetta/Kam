using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Evaluation;

public sealed class SkillTestService : ISkillTestService
{
    private readonly ISkillRegistry _registry;
    private readonly ISkillExecutionPipeline _pipeline;
    private readonly ISkillEvalCaseCatalog _evalCaseCatalog;
    private readonly ISkillAuditLogService _auditLogService;

    public SkillTestService(
        ISkillRegistry registry,
        ISkillExecutionPipeline pipeline,
        ISkillEvalCaseCatalog evalCaseCatalog,
        ISkillAuditLogService auditLogService)
    {
        _registry = registry;
        _pipeline = pipeline;
        _evalCaseCatalog = evalCaseCatalog;
        _auditLogService = auditLogService;
    }

    public async Task<SkillResult> TestAsync(
        string skillId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return SkillResult.Failed(
                "Skill id is required.",
                SkillExecutionStatus.ValidationFailed,
                "skill_id_required");
        }

        if (!_registry.TryGet(skillId, out var manifest) || manifest is null)
        {
            return SkillResult.Failed(
                $"Skill '{skillId}' is not registered.",
                SkillExecutionStatus.SkillNotFound,
                "skill_not_found");
        }

        var smokeCase = _evalCaseCatalog
            .CreateSmokeCases()
            .FirstOrDefault(candidate => candidate.Plan.SkillId.Equals(
                skillId,
                StringComparison.OrdinalIgnoreCase));
        if (smokeCase is null)
        {
            var missingCaseResult = SkillResult.Failed(
                $"No smoke test case is registered for skill '{skillId}'.",
                SkillExecutionStatus.ValidationFailed,
                "smoke_case_missing");
            await RecordAuditAsync(manifest, missingCaseResult, cancellationToken);
            return missingCaseResult;
        }

        var result = await _pipeline.ExecuteAsync(smokeCase.Plan, cancellationToken);
        await RecordAuditAsync(manifest, result, cancellationToken);
        return result;
    }

    private async Task RecordAuditAsync(
        KamSkillManifest manifest,
        SkillResult result,
        CancellationToken cancellationToken)
    {
        await _auditLogService.RecordAsync(new SkillAuditRecord
        {
            SkillId = manifest.Id,
            ExecutorType = manifest.ExecutorType,
            UserInput = "Skill test action",
            ActionPlanJson = "skill-test",
            Status = result.Status,
            ResultMessage = result.Success ? result.Message : result.ErrorMessage,
            ErrorCode = result.ErrorCode,
            DurationMilliseconds = result.DurationMilliseconds
        }, cancellationToken);
    }
}
