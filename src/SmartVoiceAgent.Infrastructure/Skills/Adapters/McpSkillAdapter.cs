using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

public sealed class McpSkillAdapter : ISkillAdapter
{
    public bool CanHandle(SkillSourceKind sourceKind)
    {
        return sourceKind == SkillSourceKind.Mcp;
    }

    public Task<IReadOnlyCollection<KamSkillManifest>> DiscoverAsync(
        SkillSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        var manifests = source.Skills
            .Where(skill => !string.IsNullOrWhiteSpace(skill.Name))
            .Select(skill => new KamSkillManifest
            {
                Id = $"mcp.{SkillIdNormalizer.Normalize(source.Id)}.{SkillIdNormalizer.Normalize(skill.Name)}",
                DisplayName = string.IsNullOrWhiteSpace(skill.DisplayName) ? skill.Name : skill.DisplayName,
                Description = skill.Description,
                Source = $"mcp:{source.Id}",
                ExecutorType = "mcp",
                Enabled = true,
                RiskLevel = SkillRiskLevel.Medium,
                Permissions = [SkillPermission.Network],
                Arguments = skill.Arguments.ToList(),
                TimeoutMilliseconds = 30000
            })
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<KamSkillManifest>>(manifests);
    }
}
