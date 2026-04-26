using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

public sealed class LocalSkillAdapter : ISkillAdapter
{
    public bool CanHandle(SkillSourceKind sourceKind)
    {
        return sourceKind == SkillSourceKind.LocalDirectory;
    }

    public Task<IReadOnlyCollection<KamSkillManifest>> DiscoverAsync(
        SkillSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        var manifest = CreateManifest(source, "local", "local");
        return Task.FromResult<IReadOnlyCollection<KamSkillManifest>>(
            manifest is null ? [] : [manifest]);
    }

    internal static KamSkillManifest? CreateManifest(
        SkillSourceDefinition source,
        string idPrefix,
        string executorType)
    {
        var metadata = SkillMarkdownManifestReader.Read(source.Location);
        if (metadata is null)
        {
            return null;
        }

        return new KamSkillManifest
        {
            Id = $"{idPrefix}.{SkillIdNormalizer.Normalize(metadata.Value.Name)}",
            DisplayName = metadata.Value.Name,
            Description = metadata.Value.Description,
            Source = $"{executorType}:{source.Location}",
            ExecutorType = executorType,
            Enabled = true,
            RiskLevel = SkillRiskLevel.Medium,
            Permissions = [SkillPermission.None],
            TimeoutMilliseconds = 30000
        };
    }
}
