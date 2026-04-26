using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

public sealed class SkillsShSkillAdapter : ISkillAdapter
{
    public bool CanHandle(SkillSourceKind sourceKind)
    {
        return sourceKind == SkillSourceKind.SkillsSh;
    }

    public Task<IReadOnlyCollection<KamSkillManifest>> DiscoverAsync(
        SkillSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        var manifest = LocalSkillAdapter.CreateManifest(source, "skills-sh", "skills.sh");
        return Task.FromResult<IReadOnlyCollection<KamSkillManifest>>(
            manifest is null ? [] : [manifest]);
    }
}
