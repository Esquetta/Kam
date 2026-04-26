namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class KamSkillManifest
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string ExecutorType { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public SkillRiskLevel RiskLevel { get; set; } = SkillRiskLevel.Low;

    public List<SkillPermission> Permissions { get; set; } = [];
}
