using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

public interface ISkillAdapterRegistry
{
    Task<IReadOnlyCollection<KamSkillManifest>> DiscoverAsync(
        IEnumerable<SkillSourceDefinition> sources,
        CancellationToken cancellationToken = default);
}
