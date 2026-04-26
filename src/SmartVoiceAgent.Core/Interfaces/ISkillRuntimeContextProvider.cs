using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillRuntimeContextProvider
{
    Task<SkillRuntimeContext> CreateAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default);
}
