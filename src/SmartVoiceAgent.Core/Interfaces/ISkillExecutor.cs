using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillExecutor
{
    bool CanExecute(string skillId);

    Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default);
}
