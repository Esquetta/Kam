using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillTestService
{
    Task<SkillResult> TestAsync(
        string skillId,
        CancellationToken cancellationToken = default);
}
