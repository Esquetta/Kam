using System.Collections.Concurrent;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills;

public sealed class InMemorySkillRegistry : ISkillRegistry
{
    private readonly ConcurrentDictionary<string, KamSkillManifest> _manifests =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(KamSkillManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new ArgumentException("Skill id is required.", nameof(manifest));
        }

        _manifests[manifest.Id] = manifest;
    }

    public bool TryGet(string skillId, out KamSkillManifest? manifest)
    {
        return _manifests.TryGetValue(skillId, out manifest);
    }

    public IReadOnlyCollection<KamSkillManifest> GetAll()
    {
        return _manifests.Values.ToArray();
    }
}
