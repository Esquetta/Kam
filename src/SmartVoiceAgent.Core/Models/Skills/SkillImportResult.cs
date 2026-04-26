namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillImportResult
{
    public int ImportedCount => Manifests.Count;

    public IReadOnlyCollection<KamSkillManifest> Manifests { get; init; } = [];
}
