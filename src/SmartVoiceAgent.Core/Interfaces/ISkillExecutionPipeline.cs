using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillExecutionPipeline
{
    Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default);
}
