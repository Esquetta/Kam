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
            Id = $"{idPrefix}.{SkillIdNormalizer.Normalize(metadata.Name)}",
            DisplayName = metadata.Name,
            Description = metadata.Description,
            Source = $"{executorType}:{source.Location}",
            ExecutorType = executorType,
            Enabled = false,
            ReviewRequired = true,
            Checksum = metadata.Checksum,
            InstalledFrom = metadata.InstalledFrom,
            InstalledAt = metadata.InstalledAt,
            RiskLevel = SkillRiskLevel.Medium,
            Permissions = [SkillPermission.None],
            GrantedPermissions = [],
            Arguments =
            [
                new SkillArgumentDefinition
                {
                    Name = "input",
                    Description = "The user's request for this skill.",
                    Type = SkillArgumentType.String,
                    Required = true
                },
                new SkillArgumentDefinition
                {
                    Name = "context",
                    Description = "Optional runtime context for the skill.",
                    Type = SkillArgumentType.String,
                    Required = false
                }
            ],
            TimeoutMilliseconds = 30000
        };
    }
}
