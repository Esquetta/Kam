using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Adapters;

namespace SmartVoiceAgent.Infrastructure.Skills.Importing;

public sealed class SkillImportService : ISkillImportService
{
    private readonly ISkillAdapterRegistry _adapterRegistry;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillPolicyStore _policyStore;

    public SkillImportService(
        ISkillAdapterRegistry adapterRegistry,
        ISkillRegistry skillRegistry,
        ISkillPolicyStore policyStore)
    {
        _adapterRegistry = adapterRegistry;
        _skillRegistry = skillRegistry;
        _policyStore = policyStore;
    }

    public async Task<SkillImportResult> ImportAsync(
        SkillSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var manifests = await _adapterRegistry.DiscoverAsync([source], cancellationToken);
        foreach (var manifest in manifests)
        {
            _policyStore.ApplyPolicy(manifest);
            _skillRegistry.Register(manifest);
        }

        return new SkillImportResult
        {
            Manifests = manifests.ToArray()
        };
    }
}
