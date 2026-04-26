using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

public sealed class SkillAdapterRegistry : ISkillAdapterRegistry
{
    private readonly IEnumerable<ISkillAdapter> _adapters;

    public SkillAdapterRegistry(IEnumerable<ISkillAdapter> adapters)
    {
        _adapters = adapters;
    }

    public async Task<IReadOnlyCollection<KamSkillManifest>> DiscoverAsync(
        IEnumerable<SkillSourceDefinition> sources,
        CancellationToken cancellationToken = default)
    {
        var manifests = new List<KamSkillManifest>();

        foreach (var source in sources.Where(source => source.Enabled))
        {
            var adapter = _adapters.FirstOrDefault(candidate => candidate.CanHandle(source.Kind));
            if (adapter is null)
            {
                continue;
            }

            manifests.AddRange(await adapter.DiscoverAsync(source, cancellationToken));
        }

        return manifests;
    }
}
