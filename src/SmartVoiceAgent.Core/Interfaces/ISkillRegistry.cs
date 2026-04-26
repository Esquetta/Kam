using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillRegistry
{
    void Register(KamSkillManifest manifest);

    bool TryGet(string skillId, out KamSkillManifest? manifest);

    IReadOnlyCollection<KamSkillManifest> GetAll();
}
