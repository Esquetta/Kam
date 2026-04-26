using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

public interface ISkillAdapter
{
    bool CanHandle(SkillSourceKind sourceKind);

    Task<IReadOnlyCollection<KamSkillManifest>> DiscoverAsync(
        SkillSourceDefinition source,
        CancellationToken cancellationToken = default);
}
