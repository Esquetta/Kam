using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillActionExecutor
{
    Task<SkillActionExecutionResult> ExecuteAsync(
        SkillActionPlan plan,
        CancellationToken cancellationToken = default);
}
