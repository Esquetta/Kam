using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillPolicyStore
{
    IReadOnlyCollection<SkillPolicyState> GetAll();

    SkillPolicyState? GetState(string skillId);

    void SaveState(SkillPolicyState state);

    void ApplyPolicy(KamSkillManifest manifest);
}
